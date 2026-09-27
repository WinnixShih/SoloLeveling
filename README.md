# SoloLeveling

習慣養成 App 的 MVP 後端＋簡易 Web 前端：每天完成自訂任務累積 EXP、升級、養五維屬性，配合 66 天計畫與連續達標紀錄。完整規則見 [docs/SPEC.md](docs/SPEC.md)。

## 技術

.NET 10／ASP.NET Core Controllers、EF Core 10 + PostgreSQL 16、JWT（HS256，7 天）、xUnit + FluentAssertions + Testcontainers、純 HTML + JS 前端（由 API 靜態提供）。

```
src/SoloLeveling.Domain          實體、領域規則（無框架依賴）
src/SoloLeveling.Infrastructure  EF Core DbContext、migration、結算服務、密碼雜湊
src/SoloLeveling.Api             Controllers、應用服務、JWT、排程、wwwroot 前端
tests/SoloLeveling.Domain.Tests  領域規則單元測試
tests/SoloLeveling.Api.Tests     Testcontainers 整合測試（結算、排程、全部 API）
```

## 啟動

### Docker Compose（一鍵）

```bash
docker compose up --build
```

- 前端：<http://localhost:8080/>
- Swagger：<http://localhost:8080/swagger>
- API 啟動時會自動套用 migration。

### 本機開發

需要一個 PostgreSQL（可用 `docker compose up postgres`），然後：

```bash
dotnet run --project src/SoloLeveling.Api
```

開發環境設定在 `src/SoloLeveling.Api/appsettings.Development.json`。

### 測試

```bash
dotnet test          # 整合測試會用 Testcontainers 起 PostgreSQL，需要 Docker 在跑
dotnet format --verify-no-changes
```

## 環境變數

| 名稱 | 說明 |
| --- | --- |
| `DatabaseConnectionString` | PostgreSQL 連線字串（必填） |
| `JwtSecret` | JWT 簽章金鑰，至少 32 bytes（必填；正式環境務必換掉 compose 的預設值） |
| `ASPNETCORE_ENVIRONMENT` | `Development` 會載入 `appsettings.Development.json` |

缺必填值時啟動即失敗（`ValidateOnStart`）。

## API 範例

所有端點在 `/api/v1`，除 `auth` 外都要 `Authorization: Bearer <token>`。時間戳一律 Unix 秒，日期一律 `YYYY-MM-DD`。錯誤統一為 `{ "error": { "code", "message" } }`。

```bash
# 註冊（自動建立 9 個預設任務）
curl -s localhost:8080/api/v1/auth/register -H 'Content-Type: application/json' \
  -d '{"email":"me@example.com","password":"password123","displayName":"小明","timeZoneId":"Asia/Taipei"}'

TOKEN=...   # 上面回應的 token

# 玩家總覽
curl -s localhost:8080/api/v1/me -H "Authorization: Bearer $TOKEN"

# 今日任務
curl -s localhost:8080/api/v1/today -H "Authorization: Bearer $TOKEN"

# 勾選某個任務（Check 只接受 0／1；Count 寫累計量；Limit 寫實際量或 null）
curl -s -X PUT localhost:8080/api/v1/today/quests/<questId>/progress \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"value":1}'

# 切換困難模式
curl -s -X PATCH localhost:8080/api/v1/me -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"hardMode":true}'

# 歷史與 EXP 流水
curl -s 'localhost:8080/api/v1/history?from=2026-09-01&to=2026-09-30' -H "Authorization: Bearer $TOKEN"
curl -s 'localhost:8080/api/v1/xp-events?limit=20' -H "Authorization: Bearer $TOKEN"

# 開新 66 天
curl -s -X POST localhost:8080/api/v1/program/restart -H "Authorization: Bearer $TOKEN"
```

## 實作上的決定（規格未明定處）

- **時間存放**：DB 時間戳欄位為 `bigint` Unix 毫秒（`CreatedAt`、`ArchivedAt`、`SettledAt`、`OccurredAt`），API 出口轉成秒；只在 `AppDbContext` 的 value converter 與 `Mappers` 兩處換算。`DateOnly` 類的日期（`StartDate`、`Date`、`LastSettledDate`）仍是 `date` 型別。
- **結算的達標率**：直接沿用 `DailyLog.CompletionRatio` 的日終值（進度只能寫今日，日終值即最終值），不在結算時重算，因此封存任務不會改寫歷史。
- **改時區往西**：`LastSettledDate` 只增不減，不重跑已結算的日子。
- **同一請求內的多筆 XpEvent** 時間相同，`Seq`（DB identity）只保證排序穩定，不保證與程式內建立順序一致。
- **懲罰四捨五入**：`MidpointRounding.AwayFromZero`（實際上 `XpNeeded` 皆為 20 的倍數，乘 0.15 永遠是整數）。
- **密碼**：PBKDF2-SHA256、100,000 次、16 bytes salt，格式自描述，可日後調高迭代次數。
- **JWT 不放 `nbf`**：IdentityModel 的有效期驗證用真實時鐘，測試以假時鐘簽發時會被判尚未生效。
- **XpEvent 沒有 `CreatedAt`**，`OccurredAt` 即建立時間。
- **Email 重複**：以查詢先擋，極端併發下仍可能撞到唯一索引而回 500。

## 後續

- 推播、好友、排名、卡片、專注計時器、AI 功能、原生 App、付費（規格明列第一階段不做）。
- Refresh token／登出即失效。
- 註冊的 Email 唯一索引衝突改回 409。
- 前端加離線暫存與 PWA。
