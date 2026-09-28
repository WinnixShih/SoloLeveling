# VPS 部署設計

## 目標

把 SoloLeveling 部署到自己的 VPS，手機透過 `https://solo.winnixgrowth.com` 天天使用，並讓後續的 iOS／Android 原生 App 有固定的 API 位址。工具選擇偏向「每一層都看得到、親手操作」，不追求全自動。

## 背景與決定

- 專案形態是「一個常駐的 .NET API 加一個 PostgreSQL」，有每小時排程與交易列鎖，不適合無伺服器平台（Vercel、Cloudflare Workers）。
- 平台：Hetzner 最小等級 VPS，跑 Docker Compose，前面加 Caddy 處理 HTTPS。
- 網域：沿用 `winnixgrowth.com`，註冊商從 Bluehost（實際登記在 Network Solutions）轉到 Cloudflare Registrar；Bluehost 的 WordPress 主機退掉。
- 更新流程：GitHub Actions 建 image 推到 GitHub Container Registry，VPS 只做 `pull` 加 `up`。
- 備份：每日 `pg_dump` 上傳 Cloudflare R2，保留 30 天。
- 不急著上線，先部署再做手機網頁版（另一份規格）。

## 假設

- 作業系統 Ubuntu 24.04 LTS。
- VPS 選新加坡機房，離台灣最近；比德國機房貴約 1 歐元。
- GitHub repo 是公開的，VPS 拉 image 不需要 token；若改為私有，VPS 要另設唯讀的 `read:packages` token。
- 只有一個使用者，CX22 等級（2 vCPU、4GB RAM、40GB 硬碟）足夠，資料庫與 API 跑同一台。

## 設計

### 1. 主機與網路

- Hetzner CX22 等級，Ubuntu 24.04。
- 初始化：只允許 SSH 金鑰登入、關閉密碼登入；`ufw` 只開 22、80、443；開啟 `unattended-upgrades` 自動安全更新；安裝 Docker Engine 與 compose plugin。
- Docker daemon 設 log 上限（`json-file`，`max-size`、`max-file`），避免 log 吃滿磁碟。

### 2. 網域與 DNS

- 網域轉到 Cloudflare Registrar，DNS 一併改由 Cloudflare 提供。
- 加一筆 A 記錄 `solo.winnixgrowth.com` 指到 VPS 的 IPv4，設為「DNS only」（不開橘雲代理），讓 Caddy 以 HTTP-01 驗證自動申請 Let's Encrypt 憑證。
- 主網域 `winnixgrowth.com` 先不指向任何服務，保留給之後的介紹頁或部落格。

### 3. 正式環境的 Compose

現有的根目錄 `docker-compose.yml` 留給本機開發，不改。新增 `deploy/` 目錄：

- `deploy/docker-compose.prod.yml`，三個服務：
  - `caddy`：對外開 80 與 443，掛 `Caddyfile` 與憑證 volume（`caddy_data`、`caddy_config`）。
  - `api`：不對外開 port。image 為 `ghcr.io/winnixshih/sololeveling-api:${ImageTag}`，預設 `latest`。環境變數 `ASPNETCORE_ENVIRONMENT=Production`、`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`、`DatabaseConnectionString`、`JwtSecret`。healthcheck 打 `/health`。
  - `postgres`：`postgres:16-alpine`，不對外開 port，密碼由 `${PostgresPassword:?}` 提供，資料放 `pgdata` volume。
- `deploy/Caddyfile`：`{$Domain}` 反向代理到 `api:8080`，加 HSTS 標頭。HTTP 轉 HTTPS 是 Caddy 預設行為。
- `deploy/.env.example`：`Domain`、`JwtSecret`、`PostgresPassword`、`ImageTag` 四個鍵，VPS 上複製成 `.env` 填實際值。`.env` 不進版控。

### 4. 後端配合調整

- **轉發標頭**：用框架內建的 `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` 環境變數，在 compose 設定，不改程式碼。
- **Swagger 只在 Development 開**：`Program.cs` 以 `app.Environment.IsDevelopment()` 包住 `UseSwagger`／`UseSwaggerUI`。
- **健康檢查端點 `GET /health`**：加 `AddHealthChecks().AddDbContextCheck<AppDbContext>()`，`MapHealthChecks("/health")`。不需驗證。
- **靜態檔快取**：`UseStaticFiles` 的 `OnPrepareResponse` 對所有靜態檔設 `Cache-Control: no-cache`，瀏覽器每次以 ETag 重新驗證，未變回 304。前端檔案很小，不做版本參數。

### 5. CI 與更新流程

- 新增 `.github/workflows/ci.yml`，push 到 `main` 觸發：
  1. `test`：`dotnet test`（GitHub runner 有 Docker，Testcontainers 可跑）與 `dotnet format --verify-no-changes`。
  2. `image`：`test` 通過後以根目錄 Dockerfile build，推到 `ghcr.io/winnixshih/sololeveling-api`，tag 為 `latest` 與 `sha-<7 碼 commit>`。
- VPS 更新：`docker compose pull && docker compose up -d`。migration 沿用啟動時自動套用。
- 退版：把 `.env` 的 `ImageTag` 改成某個 `sha-` tag，再 `up -d`。migration 若不可逆，退版前要先還原資料庫備份。

### 6. 備份與還原

- `deploy/backup.sh`：`docker compose exec -T postgres pg_dump -Fc` 倒出，檔名帶日期，`rclone copy` 到 R2 bucket；本機與 R2 各只留 30 天。
- cron 每天台灣時間 04:00（UTC 20:00）執行，輸出寫 log。
- rclone 的 R2 存取金鑰放在 `~/.config/rclone/rclone.conf`，不進 repo。
- 還原流程寫進文件，上線後實際演練一次：把最新備份還原到臨時資料庫，確認資料存在。

### 7. 文件

- 新增 `docs/DEPLOY.md`，依序：VPS 初始化、DNS、第一次上線、日常更新、退版、備份與還原、常見問題。
- README 與 CLAUDE.md 加上連結。

## 驗收

- 手機開 `https://solo.winnixgrowth.com` 可註冊、登入、完成任務，憑證有效。
- `http://solo.winnixgrowth.com` 自動轉到 `https://`。
- `/health` 回 200；`/swagger` 回 404。
- 改一行前端 push 到 `main`，Actions 綠燈，VPS 兩行更新後手機重整看得到改動。
- 退版到前一個 `sha-` tag 再切回 `latest`，服務正常。
- 備份腳本執行成功，R2 上有檔案，還原演練成功。
- 5432 從外部連不到；SSH 密碼登入被拒。

## 預估費用

| 項目 | 費用 | 說明 |
| --- | --- | --- |
| Hetzner CX22 | 每月約 4 到 6 歐元 | 新加坡機房較德國貴；公網 IPv4 約另加 0.6 歐元 |
| 網域 `.com` | 每年約 10 到 11 美元 | Cloudflare Registrar 成本價，轉移時付一年並延長效期 |
| Cloudflare DNS、Let's Encrypt、R2（10GB 內）、GitHub Actions 與 GHCR（公開 repo） | 免費 | 用量遠低於免費額度 |

合計每月約 5 到 7 歐元，每年含網域約台幣 2500 到 3000 元。之後可能增加：Hetzner 快照備份（月費 20%）、iOS 上架的 Apple Developer Program（每年 99 美元）。

## 這次不做

refresh token、PWA 與手機版面、監控告警、push 後自動 SSH 部署、Cloudflare 橘雲代理與防火牆規則、Hetzner 快照。
