# SoloLeveling 專案指引

.NET 10 習慣養成 App 的 MVP：每天完成自訂任務累積 EXP、升級、養五維屬性，配合 66 天計畫與連續達標紀錄；後端 API＋由 API 靜態提供的純 HTML＋JS 前端。

- 產品規格（領域規則、API、結算演算法、驗收清單）：[docs/SPEC.md](docs/SPEC.md)
- 啟動方式、環境變數、curl 範例、實作上的決定、後續待辦：[README.md](README.md)
- 分層、請求生命週期、資料模型、新增端點樣板：[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- 部署到 VPS、更新、退版、備份：[docs/DEPLOY.md](docs/DEPLOY.md)

## 常用指令

```bash
dotnet build
dotnet test                          # Api.Tests 用 Testcontainers 起 PostgreSQL，需要 Docker 在跑
dotnet format --verify-no-changes
docker compose up -d --build         # 第一次要先 cp .env.example .env 並填 JwtSecret；前端 http://localhost:8080/，健康檢查 /health；Production 環境不開 Swagger
dotnet run --project src/SoloLeveling.Api   # 本機開發，需先有 PostgreSQL（docker compose up -d postgres）；Swagger 只在此 Development 環境開 http://localhost:5032/swagger

# 新增 migration（設計階段會讀 appsettings.Development.json 的 JwtSecret，檔內已有）
dotnet ef migrations add <Name> -p src/SoloLeveling.Infrastructure -s src/SoloLeveling.Api -o Migrations
```

## 專案結構

依賴方向：Api → Infrastructure → Domain（Domain 無框架依賴）。

```
src/SoloLeveling.Domain           實體（Entities/）、純規則（Rules/）、enum、DefaultQuests、DomainValidationException
src/SoloLeveling.Infrastructure   AppDbContext、Migrations、SettlementService（交易＋列鎖）、PasswordHasher
src/SoloLeveling.Api              Controllers、Services（應用服務）、Contracts（DTO／Mapper）、Errors、Auth、SettlementScheduler、wwwroot 前端
tests/SoloLeveling.Domain.Tests   Domain 規則純單元測試
tests/SoloLeveling.Api.Tests      Testcontainers 整合測試（結算、排程、全部 API）
```

共用建置設定在根目錄 `Directory.Build.props`。

## 關鍵不變式（改 code 前必看）

- **Player.Xp 的任何變動都必須對應一筆 XpEvent。** Domain 規則方法（`ProgressUpdater`、`Settlement`、`Leveling`）只改記憶體中的實體並回傳事件，由 Api 服務層 `db.XpEvents.AddRange(...)` 持久化。直接呼叫 `Leveling.GainXp／LoseXp／ApplyPenalty` 時要自己補事件。
- **所有需要「今日」的端點都要先結算。** 服務層先 `db.Database.BeginTransactionAsync` → `TodayContextLoader.LoadAsync`（內部呼叫 `SettlementService.SettleAsync`，沿用呼叫端交易，以 `SELECT … FOR UPDATE` 鎖 Player 列）→ 修改 → `SaveChangesAsync` → `CommitAsync`。不需要今日的查詢（`QuestService.ListAsync`、`ReorderAsync`、`HistoryService.GetXpEventsAsync`）不結算。
- **「今日」只能用 `UserClock.DateOf(now, user.TimeZoneId)` 決定。** 時間來源一律注入 `TimeProvider`（`clock.GetUtcNow()`），禁止直接用 `DateTime.Now`／`DateTime.UtcNow`。
- **時間戳單位：** DB 是 `bigint` Unix 毫秒（`AppDbContext.ConfigureConventions` 的 value converter，全域套在 `DateTimeOffset`），API 出口是 Unix 秒（`Contracts/Mappers.ToUnixSeconds`）；只有這兩處可以換算。`DateOnly` 日期欄位（`StartDate`、`Date`、`LastSettledDate`）是 `date` 型別，API 用 `YYYY-MM-DD`。
- **不硬刪 Quest／DailyLog。** 任務封存用 `IsArchived`＋`ArchivedAt`；已結算（`IsSettled`）的 DailyLog 不可再改。
- **前端不計算 EXP／等級**，一律以 API 回傳值覆蓋畫面。
- **錯誤格式 `{ error: { code, message } }`：** Api 層丟 `ApiErrorException`（有 `BadRequest／Unauthorized／NotFound／Conflict` 工廠方法），Domain 層丟 `DomainValidationException`（對應 400），由 `ErrorHandlingMiddleware` 轉換。模型繫結失敗與 JWT 401 也在 `Program.cs` 轉成同一格式。其他例外交給框架回 500。
- **enum 序列化：** `StatType` 在 JSON 是代碼 `STR/VIT/INT/WIL/SPI`（`StatTypeJsonConverter`，在 `Program.cs` 必須註冊在 `JsonStringEnumConverter` 之前）；其他 enum 是字串名稱；DB 內 enum 一律存字串（`HasConversion<string>()`）。

## 已知陷阱

- **導覽集合加入的新實體會被當成 Modified。** 透過 `log.Progresses.Add(...)` 加入且主鍵已設值的實體，EF 會當成既有資料，`SaveChanges` 丟 `DbUpdateConcurrencyException`。要明確 `db.QuestProgresses.AddRange(...)`（見 `TodayService.SetProgressAsync`：先記下既有 Id，再把新增的標成 Added）。
- **JWT 不放 `nbf`。** IdentityModel 驗有效期用真實時鐘，測試用 `FakeTimeProvider` 簽發會被判「尚未生效」（`JwtTokenService.CreateToken`）。
- **同一請求內多筆 XpEvent 的 `OccurredAt` 相同**，`Seq`（DB identity）只保證排序穩定，不保證等於程式內建立順序；測試不要驗同一時間點內的先後。
- **Programs 有 partial unique index（每人只能一筆 `IsActive = true`）。** 換週期要先把舊的設 false 並 `SaveChangesAsync`，再新增新的（見 `ProgramService.RestartAsync`）。
- **呼叫 `TodayContextLoader.LoadAsync` 前一定要先開交易。** 沒開的話 `SettlementService` 會自己開交易並 commit，Player 列鎖在載入今日、套規則之前就釋放，後續修改不再受鎖保護。
- **`SettlementResult.TodayLog` 與 `TodayContext` 裡的實體都已被 DbContext 追蹤**，直接改屬性再 `SaveChangesAsync` 即可，不要再 `Attach`／`Update`。
- **實體 `Program` 與 Api 的進入點 `Program` 同名。** Api 內參照實體時用別名 `using ProgramEntity = SoloLeveling.Domain.Entities.Program;`。
- **建置很嚴格：** `Directory.Build.props` 開了 `TreatWarningsAsErrors`、`EnforceCodeStyleInBuild`，src 專案 `GenerateDocumentationFile`（public 成員缺 XML 註解就建置失敗）；`.editorconfig` 對 `Migrations/` 關閉 analyzer；換行一律 LF（`.gitattributes` 的 `eol=lf`＋`.editorconfig`）。

## 測試慣例

- `Domain.Tests`：純單元測試，不碰 DB。
- `Api.Tests`：
  - `PostgresFixture` 以 Testcontainers 起 `postgres:16-alpine` 並跑 migration；測試類別加 `[Collection(PostgresCollection.Name)]`，全部共用同一個容器。
  - `ApiFactory`（`WebApplicationFactory<Program>`）把 `TimeProvider` 換成 `FakeTimeProvider`（`_factory.Clock.Advance(...)` 撥時間，起始 2026-09-28 10:00 UTC），並移除 `SettlementScheduler` 的背景執行（排程另有 `SettlementSchedulerTests` 直接呼叫 `RunOnceAsync`）。
  - `_factory.RegisterAsync(timeZoneId = "UTC")` 以隨機 Email 註冊並回傳帶 Bearer 的 `HttpClient`；因為每個測試用不同使用者，共用 DB 不互相干擾。
  - 不經 HTTP 的測試（`SettlementServiceTests`）直接用 `fixture.CreateDbContext()` 種資料，並以 `new SettlementService(db, new FakeTimeProvider(now))` 呼叫。
- 測試名稱用中文描述行為（例：`GetToday_新玩家_9個任務皆未完成`），斷言用 FluentAssertions。
- 新功能照 TDD：先寫失敗的測試，再實作。

## 本專案與全域規則不同處

- **分支：** 目前直接 commit 到 `main`，不開 feature branch／PR（使用者指示）。
- **git 身分：** 個人帳號 `WinnixShih <920808wx@gmail.com>`，由 `~/.gitconfig` 的 `includeIf gitdir:D:/Github/` 自動套用；commit 前可用 `git config user.email` 確認不是公司信箱。
- **仍需遵守：** push 前要使用者明確同意；commit message 格式照全域規則。
- **識別型別：** 主鍵一律 `Guid`（規格定義），不適用全域的 int MemberId 規則。
- **資料庫：** PostgreSQL，不是 Mongo，全域的 Mongo 命名規則不適用；但 table／欄位同樣是 PascalCase（EF 預設，不要加 snake_case convention）。

## 目前狀態

- MVP 規格全部完成，測試全綠（109 個）。
- GitHub 遠端 `origin` 是 `git@github.com:WinnixShih/SoloLeveling.git`，`main` 已 push 並追蹤 `origin/main`。
- 待辦見 README「後續」：註冊 Email 唯一索引在極端併發下撞到會回 500（應改 409）、refresh token／登出即失效、前端離線暫存與 PWA 等。
