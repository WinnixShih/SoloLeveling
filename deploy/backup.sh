#!/usr/bin/env bash
# 每日備份：pg_dump 出 custom 格式檔，上傳 R2，本機與遠端各留 30 天
# 用法：backup.sh [--no-upload]
#   --no-upload  只倒檔不上傳（本機演練用）
# 環境變數（皆可省略）：
#   ComposeFile   compose 檔路徑，預設同目錄的 docker-compose.prod.yml
#   BackupDir     本機備份目錄，預設 ~/backups
#   RcloneRemote  rclone 目的地，預設 r2:sololeveling-backups
set -euo pipefail

ScriptDir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ComposeFile="${ComposeFile:-$ScriptDir/docker-compose.prod.yml}"
BackupDir="${BackupDir:-$HOME/backups}"
RcloneRemote="${RcloneRemote:-r2:sololeveling-backups}"
KeepDays=30

mkdir -p "$BackupDir"
File="$BackupDir/sololeveling-$(date -u +%Y%m%d-%H%M%S).dump"
# 先寫暫存檔，pg_dump 中途失敗不會留下檔名正常的半截檔
Tmp="$File.tmp"
trap 'rm -f "$Tmp"' EXIT

docker compose -f "$ComposeFile" exec -T postgres pg_dump -U postgres -Fc sololeveling > "$Tmp"
test -s "$Tmp"
# custom 格式經 pipe 輸出時 TOC 在檔尾，能列出目錄才代表檔案完整
docker compose -f "$ComposeFile" exec -T postgres pg_restore --list < "$Tmp" > /dev/null
mv "$Tmp" "$File"
find "$BackupDir" -name 'sololeveling-*.dump' -mtime +"$KeepDays" -delete

if [[ "${1:-}" != "--no-upload" ]]; then
    rclone copy "$File" "$RcloneRemote"
    # 只清自己產生的備份檔，bucket 內其他東西不動
    rclone delete --min-age "${KeepDays}d" --include 'sololeveling-*.dump' "$RcloneRemote"
fi

echo "backup ok: $File ($(du -h "$File" | cut -f1))"
