# 部署手冊（Linode 東京）

對外網址 `https://solo.winnixgrowth.com`。VPS 上跑 Docker Compose 三個容器：Caddy（HTTPS）、API、PostgreSQL。設計背景見 `docs/superpowers/specs/2026-09-28-vps-deployment-design.md`。

## 1. 準備 VPS

1. 到 https://cloud.linode.com 建立 Linode：Region 選 Tokyo（Tokyo 3 優先，缺貨選 Tokyo 2），Image 選 Ubuntu 24.04 LTS，Plan 選 Shared CPU → Linode 2 GB（US$12／月），Label 填 `sololeveling`，Root Password 設一個強密碼（Linode 必填，之後會關掉密碼登入），SSH Keys 勾選你自己電腦的公鑰，其餘預設，Create Linode。建好後在 Network 分頁看 IPv4。
2. 第一次登入：`ssh root@<VPS IP>`。
3. 建一般使用者：

```bash
adduser --disabled-password --gecos "" deploy
usermod -aG sudo deploy
echo 'deploy ALL=(ALL) NOPASSWD:ALL' > /etc/sudoers.d/deploy
mkdir -p /home/deploy/.ssh && cp /root/.ssh/authorized_keys /home/deploy/.ssh/ && chown -R deploy:deploy /home/deploy/.ssh && chmod 700 /home/deploy/.ssh
```

   **先開第二個終端機用 `ssh deploy@<VPS IP>` 確認登得進去**，再回第一個終端機關掉 root 與密碼登入。Ubuntu 24.04 的 `sshd_config` 會先讀 `sshd_config.d/*.conf`，cloud-init 放在那裡的設定會蓋過主檔，所以要寫進 `.d` 且檔名排最前：

```bash
printf 'PasswordAuthentication no\nPermitRootLogin no\n' > /etc/ssh/sshd_config.d/00-hardening.conf
sshd -T | grep -Ei '^(passwordauthentication|permitrootlogin)'   # 兩行都要是 no
systemctl restart ssh
```

4. 換 `deploy` 登入（`ssh deploy@<VPS IP>`），裝防火牆、自動更新、rclone、Docker：

```bash
sudo apt-get update && sudo apt-get install -y ufw unattended-upgrades rclone
sudo ufw allow OpenSSH && sudo ufw allow 80/tcp && sudo ufw allow 443/tcp && sudo ufw allow 443/udp && sudo ufw --force enable
sudo dpkg-reconfigure -f noninteractive unattended-upgrades
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker deploy
```

登出再登入讓 docker 群組生效，`docker ps` 不用 sudo 就成功。

## 2. DNS

Cloudflare → winnixgrowth.com → DNS → Add record：Type `A`、Name `solo`、IPv4 `<VPS IP>`、Proxy status **DNS only**（灰雲）。開橘雲會讓 Caddy 拿不到 HTTP-01 驗證。

驗證：`nslookup solo.winnixgrowth.com 1.1.1.1` 回 VPS IP。

## 3. 第一次上線

```bash
git clone https://github.com/WinnixShih/SoloLeveling.git ~/SoloLeveling
cd ~/SoloLeveling/deploy
cp .env.example .env
```

編輯 `.env`：`Domain=solo.winnixgrowth.com`，`JwtSecret` 用 `openssl rand -base64 48` 產生，`PostgresPassword` 用 `openssl rand -base64 24` 產生，`ImageTag=latest`。

```bash
docker compose -f docker-compose.prod.yml up -d
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs -f caddy   # 看到 certificate obtained 就是憑證好了
```

驗證：`curl https://solo.winnixgrowth.com/health` 回 `Healthy`；手機開網址能註冊登入。

## 4. 日常更新

push 到 `main` 後等 GitHub Actions 綠燈，然後：

```bash
cd ~/SoloLeveling/deploy && git pull
docker compose -f docker-compose.prod.yml pull api
docker compose -f docker-compose.prod.yml up -d
docker image prune -f
```

migration 在 API 啟動時自動套用。

## 5. 退版

1. 到 GitHub Actions 找要退回的 commit，image tag 是 `sha-` 加 commit 前 7 碼。
2. `.env` 的 `ImageTag` 改成該 tag，`docker compose -f docker-compose.prod.yml up -d`。
3. 若新版加過 migration，舊版程式碼可能不認得新欄位；退版前先用第 6 節還原對應時間點的備份。
4. 修好後 `ImageTag` 改回 `latest` 再 `up -d`。

## 6. 備份與還原

### 設定 rclone 連 R2

Cloudflare → R2 → Create bucket `sololeveling-backups` → Manage R2 API Tokens → Create（Object Read & Write，限定此 bucket）。記下 Access Key ID、Secret Access Key、Account ID。

```bash
rclone config create r2 s3 provider=Cloudflare access_key_id=<AccessKeyId> secret_access_key=<Secret> endpoint=https://<AccountId>.r2.cloudflarestorage.com acl=private
rclone ls r2:sololeveling-backups   # token 只限定這個 bucket，用 lsd r2: 列全部 bucket 會被拒
```

### 手動跑一次並排程

```bash
~/SoloLeveling/deploy/backup.sh
rclone ls r2:sololeveling-backups
(crontab -l 2>/dev/null; echo '0 20 * * * /home/deploy/SoloLeveling/deploy/backup.sh >> /home/deploy/backup.log 2>&1') | crontab -
```

UTC 20:00 等於台灣 04:00。

### 還原

```bash
cd ~/SoloLeveling/deploy
rclone copy r2:sololeveling-backups/<檔名>.dump ~/backups/
docker compose -f docker-compose.prod.yml stop api
docker compose -f docker-compose.prod.yml exec -T postgres pg_restore -U postgres -d sololeveling --clean --if-exists --no-owner < ~/backups/<檔名>.dump
docker compose -f docker-compose.prod.yml start api
```

上線後請至少演練一次：還原到臨時資料庫 `restore_drill`，確認 `SELECT count(*) FROM "Users"` 有資料，再 `DROP DATABASE restore_drill`。

## 7. 常見問題

- **Caddy log 出現 `acme: error`**：DNS 還沒指到 VPS，或 Cloudflare 開了橘雲。`nslookup solo.winnixgrowth.com 1.1.1.1` 確認。
- **api 一直 `unhealthy`**：`docker compose -f docker-compose.prod.yml logs api`，常見是 `.env` 的 `JwtSecret` 不到 32 字元。
- **`docker compose pull` 回 401**：GHCR package 還是 Private，到 GitHub 的 package settings 改 Public。
- **磁碟滿**：`docker system df` 看 image，`docker image prune -a -f` 清舊版；備份目錄只留 30 天。
