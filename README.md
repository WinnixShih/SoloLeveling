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
cp .env.example .env   # 第一次：填入 JwtSecret（可用 openssl rand -base64 48 產生）
docker compose up --build
```

- `JwtSecret` 沒有預設值，`.env` 或主機環境變數沒設時 compose 直接失敗。
- 前端：<http://localhost:8080/>
- API 啟動時會自動套用 migration。
- compose 以 Production 環境執行，Swagger 不會開；要看 Swagger 用下面的本機開發方式。
- 健康檢查：<http://localhost:8080/health>（會實際查一次資料庫）。

### 正式環境

部署到 VPS 的步驟、更新、退版、備份見 [docs/DEPLOY.md](docs/DEPLOY.md)。

### 本機開發

需要一個 PostgreSQL（可用 `docker compose up postgres`，只綁 `127.0.0.1:5432`），然後：

```bash
dotnet run --project src/SoloLeveling.Api
```

Swagger 只在 Development 環境開：<http://localhost:5032/swagger>。

開發環境設定在 `src/SoloLeveling.Api/appsettings.Development.json`（不會打包進 Docker image）。

### 測試

```bash
dotnet test          # 整合測試會用 Testcontainers 起 PostgreSQL，需要 Docker 在跑
dotnet format --verify-no-changes
```

## 環境變數

| 名稱 | 說明 |
| --- | --- |
| `DatabaseConnectionString` | PostgreSQL 連線字串（必填） |
| `JwtSecret` | JWT 簽章金鑰，至少 32 字元（必填；compose 由根目錄 `.env` 或主機環境變數提供，範本見 `.env.example`） |
| `ASPNETCORE_ENVIRONMENT` | `Development` 會載入 `appsettings.Development.json` |

缺必填值時啟動即失敗（`ValidateOnStart`）。

## API 範例

所有端點在 `/api/v1`，除 `auth` 外都要 `Authorization: Bearer <token>`。時間戳一律 Unix 秒，日期一律 `YYYY-MM-DD`。錯誤統一為 `{ "error": { "code", "message" } }`。

```bash
# 註冊（不再自動建立任務，見下方建立目標）
curl -s localhost:8080/api/v1/auth/register -H 'Content-Type: application/json' \
  -d '{"email":"me@example.com","password":"password123","displayName":"小明","timeZoneId":"Asia/Taipei"}'

TOKEN=...   # 上面回應的 token

# 建立目標（引導流程的最後一步；也可只帶 basicQuestIndexes 加入基本任務）
curl -s -X POST localhost:8080/api/v1/goals -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"goals":[{"category":"Routine","answers":{"currentBedtime":"01:00","targetBedtime":"00:00","currentWakeTime":"08:00","targetWakeTime":"07:00","lengthDays":30}}],"basicQuestIndexes":[2,4,6,8]}'

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

# 獎勵總覽、圖鑑、開箱
curl -s localhost:8080/api/v1/rewards -H "Authorization: Bearer $TOKEN"
curl -s localhost:8080/api/v1/cards -H "Authorization: Bearer $TOKEN"
curl -s -X POST localhost:8080/api/v1/rewards/chests/<chestId>/open -H "Authorization: Bearer $TOKEN"

# 商店（item：Shield／EChest／Theme）
curl -s -X POST localhost:8080/api/v1/shop/purchase -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"item":"Theme","themeKey":"violet"}'

# 稱號組合、主題
curl -s -X PUT localhost:8080/api/v1/me/title -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"prefixKey":"bed-30","suffixKey":"quests-100"}'
curl -s -X PUT localhost:8080/api/v1/me/theme -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"themeKey":"violet"}'
```

## 實作上的決定（規格未明定處）

- **時間存放**：DB 時間戳欄位為 `bigint` Unix 毫秒（`CreatedAt`、`ArchivedAt`、`SettledAt`、`OccurredAt`），API 出口轉成秒；只在 `AppDbContext` 的 value converter 與 `Mappers` 兩處換算。`DateOnly` 類的日期（`StartDate`、`Date`、`LastSettledDate`）仍是 `date` 型別。
- **結算的達標率**：直接沿用 `DailyLog.CompletionRatio` 的日終值（進度只能寫今日，日終值即最終值），不在結算時重算，因此封存任務不會改寫歷史。
- **改時區往西**：`LastSettledDate` 只增不減，不重跑已結算的日子。
- **同一請求內的多筆 XpEvent** 時間相同，`Seq`（DB identity）只保證排序穩定，不保證與程式內建立順序一致。
- **懲罰四捨五入**：`MidpointRounding.AwayFromZero`（實際上 `XpNeeded` 皆為 20 的倍數，乘 0.15 永遠是整數）。
- **密碼**：PBKDF2-SHA256、100,000 次、16 bytes salt，格式自描述，可日後調高迭代次數。
- **JWT 有效期驗證用注入的 `TimeProvider`**：`Program.cs` 的 `LifetimeValidator` 取代 IdentityModel 預設的系統時鐘，測試以假時鐘簽發與撥時間才一致。
- **XpEvent 沒有 `CreatedAt`**，`OccurredAt` 即建立時間。
- **Email 重複**：以查詢先擋，極端併發下仍可能撞到唯一索引而回 500。
- **升級寶箱以 PeakLevel 判定**：只對超過歷史最高等級的等級發 E 箱與晉階獎勵，撤銷降級後再升回來不重發；migration 把既有玩家的 PeakLevel 設為當時等級。
- **成就以狀態判定**：條件成立且未解鎖就解鎖，因此上線前已達成的條件會在下一次請求補解鎖一次（含連續 7／30 天的寶箱）。
- **達標金幣收回最多扣到 0**：金幣可能已花掉，收回時不讓餘額變負，事件金額等於實際扣除量。
- **保險卡只在連勝進行中消耗**：Streak 為 0 時沒有東西可保護，不消耗；晉階送的保險卡受上限 3 截斷。
- **封存回 200**：`DELETE /quests/{id}`、`DELETE /goals/{id}` 回 `{ rewards }`，封存造成的達標金幣與升級訊息才不會遺失。
- **卡片插畫**：放 `wwwroot/cards/{id}.webp`（2:3、768×1152，不含邊框與文字）；缺檔時前端顯示稀有度色塊與名稱。

## 後續

- 推播、好友、排名、專注計時器、AI 功能、原生 App、付費（規格明列第一階段不做）。
- Refresh token／登出即失效。
- 註冊的 Email 唯一索引衝突改回 409。
- 前端加離線暫存與 PWA。
- GET /today、GET /me 每次讀取都套用獎勵判定（約 9 次查詢），之後可在無狀態變更時略過統計查詢。
- 獎勵系統之後可能加：地下城、66 天 Boss、更多主題、稱號特效、以金幣兌換指定卡片。
