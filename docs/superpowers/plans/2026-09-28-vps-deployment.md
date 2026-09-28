# VPS 部署實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 SoloLeveling 部署到 Linode（Akamai）東京 VPS，以 `https://solo.winnixgrowth.com` 對外提供服務，並建立更新、退版、備份的固定流程。

**Architecture:** VPS 上跑 Docker Compose 三個容器：Caddy（HTTPS 反向代理）、API（從 GitHub Container Registry 拉 image）、PostgreSQL。GitHub Actions 在 push 到 `main` 時跑測試並建 image；VPS 只做 `pull` 加 `up`。每日 `pg_dump` 上傳 Cloudflare R2。

**Tech Stack:** .NET 10、Docker Compose、Caddy 2、PostgreSQL 16、GitHub Actions、GHCR、rclone、Cloudflare R2、Linode（Akamai）、Ubuntu 24.04

**Spec:** `docs/superpowers/specs/2026-09-28-vps-deployment-design.md`

## Global Constraints

- 建置開了 `TreatWarningsAsErrors` 與 `EnforceCodeStyleInBuild`；src 專案 public 成員缺 XML 註解就建置失敗。
- 換行一律 LF；修改既有檔案用 Edit 工具。
- 自家設定鍵一律 PascalCase，不用底線：`Domain`、`JwtSecret`、`PostgresPassword`、`ImageTag`、`ApiImage`。框架固定名稱（`ASPNETCORE_ENVIRONMENT`、`ASPNETCORE_FORWARDEDHEADERS_ENABLED`）原樣保留。
- 時間來源一律注入 `TimeProvider`，禁止 `DateTime.Now`／`DateTime.UtcNow`。
- 註解與文件用繁體中文、全形標點；commit message 格式 `type: 主旨`，結尾加 `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`。
- 分批 commit，commit 後不 push；push 一律要使用者當下同意。
- 根目錄 `docker-compose.yml`、`.env.example` 留給本機開發，不改。
- 測試名稱用中文描述行為，斷言用 FluentAssertions；整合測試類別加 `[Collection(PostgresCollection.Name)]`。

## Review Focus

1. **未登入呼叫 `/health`** 應回 200，不能被 JWT 擋成 401（監控與 compose healthcheck 都不帶 token）。→ Task 1 測試 `Health_未登入_回200`。
2. **`/` 與 `/app.js` 都要帶 `Cache-Control: no-cache`**，`UseDefaultFiles` 改寫成 `/index.html` 後仍要經過同一個 `OnPrepareResponse`。→ Task 1 測試 `StaticFiles_根路徑與appjs_都帶no_cache`。
3. **`.env` 缺 `PostgresPassword` 或 `JwtSecret`** 時 compose 必須直接失敗，不能用空密碼把 DB 跑起來。→ Task 2 步驟 5 用 `docker compose config` 驗證。
4. **備份檔必須能還原**，`pg_dump` 成功不代表檔案可用。→ Task 4 步驟 4 在本機做一次 `pg_restore` 演練。
5. **退版到舊 `sha-` tag 後服務仍要能起來**，migration 只往前套用，退版前要先確認該版沒有不相容的 schema。→ Task 6 驗收清單包含退版再切回。

---

### Task 1: 後端配合調整（`/health`、Swagger 限 Development、靜態檔 no-cache）

**Files:**
- Modify: `src/SoloLeveling.Api/SoloLeveling.Api.csproj`
- Modify: `src/SoloLeveling.Api/Program.cs:86-108`
- Modify: `Dockerfile:14`
- Modify: `tests/SoloLeveling.Api.Tests/ApiFactory.cs:16-33`
- Create: `tests/SoloLeveling.Api.Tests/OperationsApiTests.cs`

**Interfaces:**
- Consumes: 既有 `ApiFactory(string connectionString)`、`PostgresFixture.ConnectionString`。
- Produces: `ApiFactory(string connectionString, string environment = "Development")`；`GET /health` 回 200 純文字 `Healthy`；容器內有 `curl` 供 Task 2 的 healthcheck 使用。

- [ ] **Step 1: 讓 `ApiFactory` 可以指定環境**

把 `tests/SoloLeveling.Api.Tests/ApiFactory.cs` 的類別宣告與 `ConfigureWebHost` 改成：

```csharp
/// <summary>
/// 對著 Testcontainers 的 PostgreSQL 起整個 API；時間來源換成 <see cref="FakeTimeProvider"/> 讓測試可撥時間。
/// <paramref name="environment"/> 預設 Development，要驗證正式環境行為（例如 Swagger 關閉）時傳 "Production"。
/// </summary>
public sealed class ApiFactory(string connectionString, string environment = "Development") : WebApplicationFactory<Program>
{
    public const string JwtSecret = "test-secret-key-must-be-at-least-32-bytes-long!!";

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("DatabaseConnectionString", connectionString);
        builder.UseSetting("JwtSecret", JwtSecret);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            // 排程結算不在測試 host 內背景執行，避免跟測試本身的結算互相干擾；排程邏輯另有專屬測試
            var scheduler = services.Single(d => d.ImplementationType == typeof(SettlementScheduler));
            services.Remove(scheduler);
        });
    }
```

（`RegisterAsync` 不動。）

- [ ] **Step 2: 寫失敗的測試**

建立 `tests/SoloLeveling.Api.Tests/OperationsApiTests.cs`：

```csharp
using System.Net;
using FluentAssertions;

namespace SoloLeveling.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class OperationsApiTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Health_未登入_回200()
    {
        using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task Swagger_Development環境_回200()
    {
        using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Swagger_Production環境_回404()
    {
        using var factory = new ApiFactory(fixture.ConnectionString, "Production");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/app.js")]
    public async Task StaticFiles_根路徑與appjs_都帶no_cache(string path)
    {
        using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoCache.Should().BeTrue();
    }
}
```

- [ ] **Step 3: 跑測試確認失敗**

Run: `dotnet test tests/SoloLeveling.Api.Tests --filter "FullyQualifiedName~OperationsApiTests"`
Expected: `Health_未登入_回200` 回 404、`Swagger_Production環境_回404` 回 200、`StaticFiles_*` 的 CacheControl 為 null，三類都 FAIL；`Swagger_Development環境_回200` PASS。

- [ ] **Step 4: 加健康檢查套件**

在 `src/SoloLeveling.Api/SoloLeveling.Api.csproj` 的第一個 `<ItemGroup>` 加：

```xml
    <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore" Version="10.0.12" />
```

跑 `dotnet restore`。若 10.0.12 不存在，用 `dotnet package search Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore --prerelease false` 找最新的 10.0.x。

- [ ] **Step 5: 改 `Program.cs`**

在 `builder.Services.AddSwaggerGen();` 之後加：

```csharp
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
```

把 `app.UseMiddleware<ErrorHandlingMiddleware>();` 到 `app.MapControllers();` 這段改成：

```csharp
app.UseMiddleware<ErrorHandlingMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseDefaultFiles();
// 前端只有三個小檔案，一律要求瀏覽器重新驗證（ETag 未變回 304），部署新版後不會拿到舊檔
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
// 給 compose healthcheck 與部署後驗證用，會實際查一次資料庫
app.MapHealthChecks("/health");
```

- [ ] **Step 6: 跑測試確認通過**

Run: `dotnet test tests/SoloLeveling.Api.Tests --filter "FullyQualifiedName~OperationsApiTests"`
Expected: 5 個測試全部 PASS。

再跑全部：`dotnet build && dotnet format --verify-no-changes && dotnet test`
Expected: 0 錯誤、format 無差異、114 個測試通過。

- [ ] **Step 7: Dockerfile 加 curl**

把 `Dockerfile` 的 `RUN apt-get update ...` 那行改成：

```dockerfile
# Npgsql 啟動時會探測 Kerberos 函式庫，沒裝會在 log 印一行 Error（不影響功能）；curl 給 compose healthcheck 用
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 curl && rm -rf /var/lib/apt/lists/*
```

Run: `docker build -t sololeveling-api:local . && docker run --rm sololeveling-api:local curl --version | head -1`
Expected: 印出 curl 版本。

- [ ] **Step 8: Commit**

```bash
git add src/SoloLeveling.Api/SoloLeveling.Api.csproj src/SoloLeveling.Api/Program.cs Dockerfile tests/SoloLeveling.Api.Tests/ApiFactory.cs tests/SoloLeveling.Api.Tests/OperationsApiTests.cs
git commit -m "feat: 新增 /health 端點、Swagger 限 Development 並讓靜態檔 no-cache" -m "1. 部署到反向代理後需要健康檢查給 compose 與監控用，加 /health 並實際查 DB
2. 正式環境不需對外暴露 API 說明，Swagger 只在 Development 開
3. 前端沒有版本參數，更新後手機可能拿舊檔，靜態檔一律 no-cache 走 ETag 重新驗證" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: 正式環境 Compose、Caddyfile、`.env.example`

**Files:**
- Create: `deploy/docker-compose.prod.yml`
- Create: `deploy/Caddyfile`
- Create: `deploy/.env.example`

**Interfaces:**
- Consumes: Task 1 的 `GET /health` 與容器內 `curl`；image 名稱 `ghcr.io/winnixshih/sololeveling-api`（Task 3 會推）。
- Produces: `.env` 四個鍵 `Domain`、`JwtSecret`、`PostgresPassword`、`ImageTag`，以及本機測試用的選填鍵 `ApiImage`；Task 4 的備份腳本以 `docker compose -f deploy/docker-compose.prod.yml exec postgres` 存取資料庫。

- [ ] **Step 1: 寫 compose**

建立 `deploy/docker-compose.prod.yml`：

```yaml
# 正式環境：Caddy 對外提供 HTTPS，API 與 PostgreSQL 都不對外開 port
# 使用方式：cp .env.example .env 填值後 docker compose -f docker-compose.prod.yml up -d
services:
  caddy:
    image: caddy:2-alpine
    restart: unless-stopped
    ports:
      - "80:80"
      - "443:443"
      - "443:443/udp"
    environment:
      Domain: "${Domain:?請在 .env 設定 Domain，例如 solo.winnixgrowth.com}"
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy_data:/data
      - caddy_config:/config
    depends_on:
      api:
        condition: service_healthy

  api:
    # ApiImage 只在本機驗證 compose 時用來換成本機 build 的 image，正式環境不設
    image: "${ApiImage:-ghcr.io/winnixshih/sololeveling-api}:${ImageTag:-latest}"
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      # 在 Caddy 後面，讓框架從 X-Forwarded-* 還原原始 scheme 與來源 IP
      ASPNETCORE_FORWARDEDHEADERS_ENABLED: "true"
      DatabaseConnectionString: "Host=postgres;Port=5432;Database=sololeveling;Username=postgres;Password=${PostgresPassword:?請在 .env 設定 PostgresPassword}"
      JwtSecret: "${JwtSecret:?請在 .env 設定 JwtSecret（至少 32 字元），可用 openssl rand -base64 48 產生}"
    healthcheck:
      test: ["CMD", "curl", "-fsS", "http://localhost:8080/health"]
      interval: 15s
      timeout: 5s
      retries: 5
      start_period: 30s
    depends_on:
      postgres:
        condition: service_healthy
    logging:
      driver: json-file
      options:
        max-size: "10m"
        max-file: "3"

  postgres:
    image: postgres:16-alpine
    restart: unless-stopped
    environment:
      POSTGRES_DB: sololeveling
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: "${PostgresPassword:?請在 .env 設定 PostgresPassword}"
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d sololeveling"]
      interval: 5s
      timeout: 3s
      retries: 10
    logging:
      driver: json-file
      options:
        max-size: "10m"
        max-file: "3"

volumes:
  pgdata:
  caddy_data:
  caddy_config:
```

- [ ] **Step 2: 寫 Caddyfile**

建立 `deploy/Caddyfile`：

```
{$Domain} {
	encode gzip
	header Strict-Transport-Security "max-age=31536000; includeSubDomains"
	reverse_proxy api:8080
}
```

（Caddyfile 慣例用 tab 縮排；`.editorconfig` 若對 `*` 強制空白，加一段 `[Caddyfile]` `indent_style = tab`。）

- [ ] **Step 3: 寫 `.env.example`**

建立 `deploy/.env.example`：

```
# 複製成同目錄的 .env 後填入實際值（.env 已被根目錄 .gitignore 排除）
# Domain：對外網域，Caddy 會用它自動申請 Let's Encrypt 憑證
Domain=solo.winnixgrowth.com
# JwtSecret：JWT 簽章金鑰，至少 32 字元，可用 `openssl rand -base64 48` 產生
JwtSecret=
# PostgresPassword：資料庫密碼，可用 `openssl rand -base64 24` 產生
PostgresPassword=
# ImageTag：要跑的 API image tag，平常 latest，退版時改成 Actions 產出的 sha-xxxxxxx
ImageTag=latest
```

- [ ] **Step 4: 確認 `.gitignore` 涵蓋 `deploy/.env`**

Run: `cd deploy && printf 'Domain=x\nJwtSecret=y\nPostgresPassword=z\n' > .env && git check-ignore -v .env; cd ..`
Expected: 印出 `.gitignore:7:.env	deploy/.env`。（根目錄 `.gitignore` 的 `.env` 沒有前置 `/`，會匹配所有子目錄。）

- [ ] **Step 5: 驗證缺值會失敗**

Run: `cd deploy && rm .env && docker compose -f docker-compose.prod.yml config > /dev/null; echo "exit $?"; cd ..`
Expected: exit 1，錯誤訊息提到 `Domain` 必填。

Run: `cd deploy && printf 'Domain=localhost\nJwtSecret=0123456789abcdef0123456789abcdef\n' > .env && docker compose -f docker-compose.prod.yml config > /dev/null; echo "exit $?"; cd ..`
Expected: exit 1，錯誤訊息提到 `PostgresPassword` 必填。

- [ ] **Step 6: 本機用 localhost 跑一次完整堆疊**

Task 1 已 build 出 `sololeveling-api:local`。Caddy 對 `localhost` 會用內建 CA 簽自簽憑證，不會去打 Let's Encrypt。

```bash
cd deploy
printf 'Domain=localhost\nJwtSecret=0123456789abcdef0123456789abcdef\nPostgresPassword=localtest\nApiImage=sololeveling-api\nImageTag=local\n' > .env
docker compose -f docker-compose.prod.yml -p sololeveling-prod up -d
sleep 20
docker compose -f docker-compose.prod.yml -p sololeveling-prod ps
curl -sk https://localhost/health; echo
curl -sk -o /dev/null -w '%{http_code}\n' https://localhost/swagger/index.html
curl -sI http://localhost/ | head -1
curl -skI https://localhost/app.js | grep -i cache-control
```

Expected：三個容器 `running`，api 為 `healthy`；`/health` 印 `Healthy`；swagger 回 404；HTTP 回 `308` 轉址；`app.js` 有 `cache-control: no-cache`。

用 `-p sololeveling-prod` 是為了跟根目錄本機開發的 compose 專案分開，volume 不會撞名。

- [ ] **Step 7: 清掉本機驗證環境**

```bash
docker compose -f docker-compose.prod.yml -p sololeveling-prod down -v
rm .env
cd ..
```

- [ ] **Step 8: Commit**

```bash
git add deploy/docker-compose.prod.yml deploy/Caddyfile deploy/.env.example
git commit -m "chore: 新增正式環境 compose 與 Caddy 反向代理設定" -m "1. 對外只開 Caddy 的 80／443，API 與 PostgreSQL 不對外，憑證由 Caddy 自動申請
2. image 從 GHCR 拉，tag 由 .env 的 ImageTag 決定，方便退版
3. JwtSecret、PostgresPassword、Domain 缺值時 compose 直接失敗" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: GitHub Actions：測試通過後建 image 推 GHCR

**Files:**
- Create: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: 根目錄 `Dockerfile`；`dotnet test` 需要 Docker（Testcontainers）。
- Produces: `ghcr.io/winnixshih/sololeveling-api:latest` 與 `ghcr.io/winnixshih/sololeveling-api:sha-<7 碼>`，供 Task 2 的 compose 與 Task 6 拉取。

- [ ] **Step 1: 寫 workflow**

建立 `.github/workflows/ci.yml`：

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:

permissions:
  contents: read
  packages: write

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - run: dotnet restore
      - run: dotnet build --no-restore
      - run: dotnet format --verify-no-changes --no-restore
      # Api.Tests 用 Testcontainers 起 PostgreSQL，ubuntu-latest runner 內建 Docker
      - run: dotnet test --no-build

  image:
    needs: test
    if: github.event_name == 'push' && github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      - id: meta
        uses: docker/metadata-action@v5
        with:
          # metadata-action 會把 owner 轉成小寫，GHCR 要求小寫
          images: ghcr.io/${{ github.repository_owner }}/sololeveling-api
          tags: |
            type=raw,value=latest
            type=sha,prefix=sha-,format=short
      - uses: docker/build-push-action@v6
        with:
          context: .
          push: true
          tags: ${{ steps.meta.outputs.tags }}
          labels: ${{ steps.meta.outputs.labels }}
          cache-from: type=gha
          cache-to: type=gha,mode=max
```

- [ ] **Step 2: 本機檢查 YAML 語法**

Run: `python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/ci.yml')); print('ok')"`
Expected: `ok`。（沒有 PyYAML 就 `pip install pyyaml`，或跳過這步，交給下一步的 Actions 驗證。）

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "chore: 新增 GitHub Actions 跑測試並推 image 到 GHCR" -m "1. push 到 main 先跑 build、format、test，通過才建 image
2. image 打 latest 與 sha-<commit> 兩個 tag，VPS 退版時用 sha tag" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

- [ ] **Step 4: 取得使用者同意後 push，觀察 Actions**

先跟使用者確認可以 push。push 後：

Run: `gh run watch --exit-status`（或到 GitHub 的 Actions 頁看）
Expected: `test` 與 `image` 兩個 job 綠燈。

- [ ] **Step 5: 把 GHCR package 設為 Public**

GHCR 第一次推上去的 package 預設是 Private，即使 repo 是 Public。VPS 匿名拉 image 會 401。

到 `https://github.com/WinnixShih?tab=packages` 點 `sololeveling-api` → Package settings → Danger Zone → Change visibility → Public。

驗證：`docker logout ghcr.io; docker pull ghcr.io/winnixshih/sololeveling-api:latest`
Expected: 不用登入就拉得到。

---

### Task 4: 備份腳本與還原演練

**Files:**
- Create: `deploy/backup.sh`

**Interfaces:**
- Consumes: Task 2 的 compose 檔與 `postgres` 服務名。
- Produces: `deploy/backup.sh [--no-upload]`，環境變數 `ComposeFile`（預設同目錄的 `docker-compose.prod.yml`）、`BackupDir`（預設 `~/backups`）、`RcloneRemote`（預設 `r2:sololeveling-backups`）；備份檔名 `sololeveling-<UTC 時間>.dump`，`pg_dump` custom 格式。

- [ ] **Step 1: 寫腳本**

建立 `deploy/backup.sh`：

```bash
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

docker compose -f "$ComposeFile" exec -T postgres pg_dump -U postgres -Fc sololeveling > "$File"
test -s "$File"
find "$BackupDir" -name 'sololeveling-*.dump' -mtime +"$KeepDays" -delete

if [[ "${1:-}" != "--no-upload" ]]; then
    rclone copy "$File" "$RcloneRemote"
    rclone delete --min-age "${KeepDays}d" "$RcloneRemote"
fi

echo "backup ok: $File ($(du -h "$File" | cut -f1))"
```

Run: `chmod +x deploy/backup.sh && git update-index --chmod=+x deploy/backup.sh`（Windows 上 git 才會記住可執行位元。）

- [ ] **Step 2: 本機演練備份**

用根目錄的開發 compose（postgres 服務名相同）當來源：

```bash
docker compose up -d postgres
ComposeFile=docker-compose.yml BackupDir=/tmp/solo-backup ./deploy/backup.sh --no-upload
ls -la /tmp/solo-backup
```

Expected: 印 `backup ok: /tmp/solo-backup/sololeveling-<時間>.dump (<大小>)`，檔案大小大於 0。

- [ ] **Step 3: 確認備份檔是有效的 custom 格式**

Run: `docker compose exec -T postgres pg_restore --list < /tmp/solo-backup/sololeveling-*.dump | head -20`
Expected: 列出 `TABLE public Users`、`TABLE public Players` 等項目。

- [ ] **Step 4: 還原演練（Review Focus 4）**

還原到一個臨時資料庫，不動原本的：

```bash
docker compose exec -T postgres psql -U postgres -c 'DROP DATABASE IF EXISTS restore_drill;'
docker compose exec -T postgres psql -U postgres -c 'CREATE DATABASE restore_drill;'
docker compose exec -T postgres pg_restore -U postgres -d restore_drill --no-owner < /tmp/solo-backup/sololeveling-*.dump
docker compose exec -T postgres psql -U postgres -d restore_drill -c 'SELECT count(*) FROM "Users";'
docker compose exec -T postgres psql -U postgres -c 'DROP DATABASE restore_drill;'
rm -rf /tmp/solo-backup
```

Expected: `count` 等於本機開發資料庫的使用者數（至少是前面煙霧測試註冊的幾個）。

- [ ] **Step 5: Commit**

```bash
git add deploy/backup.sh
git commit -m "chore: 新增每日 pg_dump 備份腳本" -m "1. 機器壞掉資料不能跟著沒，每日倒出 custom 格式並用 rclone 上傳 R2
2. 本機與遠端各只留 30 天，--no-upload 供本機演練" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: 部署文件

**Files:**
- Create: `docs/DEPLOY.md`
- Modify: `README.md:17-30`（「啟動」區加正式環境連結）
- Modify: `CLAUDE.md:3-7`（文件清單加一行）

**Interfaces:**
- Consumes: Task 2 的 `.env` 鍵、Task 3 的 image 名稱與 tag 規則、Task 4 的腳本介面。
- Produces: 可照著做的操作手冊；Task 6 直接依此執行。

- [ ] **Step 1: 寫 `docs/DEPLOY.md`**

```markdown
# 部署手冊（Linode 東京）

對外網址 `https://solo.winnixgrowth.com`。VPS 上跑 Docker Compose 三個容器：Caddy（HTTPS）、API、PostgreSQL。設計背景見 `docs/superpowers/specs/2026-09-28-vps-deployment-design.md`。

## 1. 準備 VPS

1. 到 https://cloud.linode.com 建立 Linode：Region 選 Tokyo（Tokyo 3 優先，缺貨選 Tokyo 2），Image 選 Ubuntu 24.04 LTS，Plan 選 Shared CPU → Linode 2 GB（US$12／月），Label 填 `sololeveling`，Root Password 設一個強密碼（Linode 必填，之後會關掉密碼登入），SSH Keys 勾選你自己電腦的公鑰，其餘預設，Create Linode。建好後在 Network 分頁看 IPv4。
2. 第一次登入：`ssh root@<VPS IP>`。
3. 建一般使用者並關掉 root 密碼登入：

```bash
adduser --disabled-password --gecos "" deploy
usermod -aG sudo deploy
echo 'deploy ALL=(ALL) NOPASSWD:ALL' > /etc/sudoers.d/deploy
mkdir -p /home/deploy/.ssh && cp /root/.ssh/authorized_keys /home/deploy/.ssh/ && chown -R deploy:deploy /home/deploy/.ssh && chmod 700 /home/deploy/.ssh
sed -i 's/^#\?PasswordAuthentication .*/PasswordAuthentication no/; s/^#\?PermitRootLogin .*/PermitRootLogin no/' /etc/ssh/sshd_config
systemctl restart ssh
```

4. 換 `deploy` 登入（`ssh deploy@<VPS IP>`），裝防火牆、自動更新、Docker：

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
rclone lsd r2:
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
```

- [ ] **Step 2: README 加連結**

在 `README.md` 的「### 本機開發」之前（「Docker Compose（一鍵）」區塊結尾）加一段：

```markdown
### 正式環境

部署到 VPS 的步驟、更新、退版、備份見 [docs/DEPLOY.md](docs/DEPLOY.md)。
```

- [ ] **Step 3: CLAUDE.md 加一行**

在 `CLAUDE.md` 第 7 行「分層、請求生命週期…」之後加：

```markdown
- 部署到 VPS、更新、退版、備份：[docs/DEPLOY.md](docs/DEPLOY.md)
```

- [ ] **Step 4: Commit**

```bash
git add docs/DEPLOY.md README.md CLAUDE.md
git commit -m "docs: 新增 VPS 部署手冊" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: 上線與驗收（VPS 操作，非程式碼）

**Files:** 無。全部依 `docs/DEPLOY.md` 操作。

**Interfaces:**
- Consumes: Task 3 已推上 GHCR 且設為 Public 的 image；Task 5 的手冊。
- Produces: 對外可用的服務；驗收結果回填規格的驗收清單。

- [ ] **Step 1: 租 VPS 並初始化**

依 `DEPLOY.md` 第 1 節操作。完成後 `ssh deploy@<VPS IP> docker ps` 成功。

- [ ] **Step 2: DNS**

依第 2 節加 A 記錄。`nslookup solo.winnixgrowth.com 1.1.1.1` 回 VPS IP。

- [ ] **Step 3: 第一次上線**

依第 3 節。Expected：`curl https://solo.winnixgrowth.com/health` 回 `Healthy`。

- [ ] **Step 4: 驗收清單**

逐項執行並記錄：

```bash
curl -sI http://solo.winnixgrowth.com/ | head -1          # 308 轉 https
curl -s https://solo.winnixgrowth.com/health              # Healthy
curl -s -o /dev/null -w '%{http_code}\n' https://solo.winnixgrowth.com/swagger/index.html   # 404
curl -sI https://solo.winnixgrowth.com/app.js | grep -i cache-control   # no-cache
nc -zv -w 3 <VPS IP> 5432                                  # 連不上
ssh -o PreferredAuthentications=password deploy@<VPS IP>   # Permission denied
```

手機開 `https://solo.winnixgrowth.com`：註冊、登入、勾一個任務、看 EXP 變化，憑證無警告。

- [ ] **Step 5: 更新流程演練**

改前端一行（例如 `index.html` 的 `<title>`），commit、經使用者同意後 push，等 Actions 綠燈，依第 4 節更新。手機重整看到改動。

- [ ] **Step 6: 退版演練（Review Focus 5）**

`.env` 的 `ImageTag` 改成上一個 `sha-` tag，`up -d`，`/health` 仍回 `Healthy`，手機看到舊標題；改回 `latest` 再 `up -d`。

- [ ] **Step 7: 備份與還原演練**

依第 6 節設定 rclone、跑一次 `backup.sh`、`rclone ls` 看到檔案、加 cron、做一次 `restore_drill` 還原。

- [ ] **Step 8: 回填規格**

在 `docs/superpowers/specs/2026-09-28-vps-deployment-design.md` 的「驗收」區每項前加 `[x]`，commit：

```bash
git add docs/superpowers/specs/2026-09-28-vps-deployment-design.md
git commit -m "docs: 回填 VPS 部署驗收結果" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```
