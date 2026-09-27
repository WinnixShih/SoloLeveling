# SoloLeveling 實作規格：習慣養成 App（MVP 後端）

本文件是實作用的規格。所有數值、規則、API 形狀都已決定，實作者不需要再做產品判斷；遇到本文未定義的細節，選最簡單可運作的做法並在 README 記錄。第一階段只做後端與可用的 Web 前端，手機 App 之後再說。

## 1. 交付範圍

必須完成：

- ASP.NET Core 10 Web API（Controllers），含 EF Core + PostgreSQL、JWT 驗證、Swagger。
- 第 4 節所有領域規則與第 7 節結算演算法，並有單元測試與整合測試。
- 第 6 節所有端點。
- 一個簡單的 Web 前端（純 HTML + JS，由 API 的 `wwwroot` 靜態提供）能完成每日操作流程：登入、看今日任務、勾選、累計、填上限值、看等級與屬性、看 66 天日曆。
- Docker Compose 一鍵啟動（api + postgres）。

不做：推播、好友、排名、卡片、專注計時器、AI 功能、iOS/Android 原生 App、付費。

## 2. 技術堆疊（固定）

| 項目 | 選擇 |
| --- | --- |
| 語言與框架 | C# 14，.NET 10，ASP.NET Core Controllers |
| ORM | EF Core 10 + Npgsql，Code-first migrations |
| 資料庫 | PostgreSQL 16 |
| 驗證 | Email + 密碼註冊登入，自寫 PBKDF2（不用 ASP.NET Core Identity）；JWT Bearer，存取權杖 7 天；密碼最少 8 碼 |
| 測試 | xUnit + FluentAssertions；整合測試用 Testcontainers 起 PostgreSQL |
| 時間 | 時間戳一律 Unix time：DB 存 `bigint` **毫秒**（UTC），API 出口一律 **秒**（10 位數）；秒／毫秒只在 Infrastructure（entity ↔ DB）與 Api（DTO）兩個邊界互轉。「日期」欄位為 `DateOnly`，DB 存 `date`，API 以 `YYYY-MM-DD` 字串表示，以使用者時區換算 |
| 時間來源 | 注入 `TimeProvider`（`GetUtcNow()`），測試可假造 |
| 專案結構 | `src/SoloLeveling.Domain`（實體、規則、無框架依賴）、`src/SoloLeveling.Infrastructure`（EF Core、Repository）、`src/SoloLeveling.Api`（含 `wwwroot` 前端）、`tests/SoloLeveling.Domain.Tests`、`tests/SoloLeveling.Api.Tests` |

## 3. 名詞定義

- 玩家（Player）：一個使用者的遊戲化狀態。
- 任務（Quest）：使用者定義的每日習慣。
- 每日紀錄（DailyLog）：某使用者某一天的快照，結算後不可修改。
- 結算（Settlement）：把一個已結束的日期轉成 DailyLog 並更新連續紀錄、EXP 的過程。
- 今日（Today）：使用者時區下的當前日期。

## 4. 領域規則（必須完全依此實作）

### 4.1 屬性與難度常數

```csv
StatType,Code
Strength,STR
Vitality,VIT
Intelligence,INT
Willpower,WIL
Spirit,SPI
```

```csv
Difficulty,XpReward,StatReward
Easy,10,1
Normal,20,1
Hard,35,2
```

屬性初始值皆為 10。玩家初始 Level = 1，Xp = 0，Streak = 0。

### 4.2 等級

升到下一級所需 EXP：`XpNeeded(level) = 100 + 20 * (level - 1)`。

加 EXP 時：`Xp += amount`；當 `Xp >= XpNeeded(Level)` 時 `Xp -= XpNeeded(Level); Level += 1`，重複直到不滿足。

扣 EXP 時（撤銷完成）：`Xp -= amount`；當 `Xp < 0` 且 `Level > 1` 時 `Level -= 1; Xp += XpNeeded(Level)`；若 `Level == 1` 且 `Xp < 0` 則 `Xp = 0`。

懲罰扣 EXP 時：不降級，`Xp = max(0, Xp - amount)`。

階級與稱號：由 API 計算回傳，不存資料庫。

```csv
LevelFrom,LevelTo,Rank,Title
1,4,E,新手
5,9,D,見習者
10,19,C,挑戰者
20,29,B,精英
30,44,A,大師
45,,S,傳說
```

### 4.3 任務類型與完成判定

```csv
QuestType,ValueMeaning,IsDone
Check,0 或 1,value >= 1
Count,累計量,value >= TargetValue
Limit,實際量 (null 表示未填),value != null && value <= TargetValue
```

`TargetValue` 與 `Step` 為 decimal(10,2)；Check 類型忽略這兩欄。

### 4.4 進度更新（PUT progress）

對某任務寫入新值時：

1. 讀取該任務今日的 QuestProgress（無則視為 value = 0 或 null）。
2. `wasDone = IsDone(old)`，`isDone = IsDone(new)`。
3. 若 `!wasDone && isDone`：新增 XpEvent(Source=Quest, Amount=+XpReward)，屬性 += StatReward，`Player.TotalCompleted += 1`，記錄 `XpGranted`、`StatGranted`。
4. 若 `wasDone && !isDone`：新增 XpEvent(Source=QuestUndo, Amount=-XpGranted)，屬性 -= StatGranted（最低 0），`Player.TotalCompleted -= 1`（最低 0），清除 XpGranted 與 StatGranted。
5. 重新計算今日達標率並寫回 DailyLog，處理達標獎勵（4.5）。
6. 以上在同一交易內完成。

只允許更新「今日」的進度；對其他日期回 409。

### 4.5 達標率、模式、達標獎勵

- `CompletionRatio = doneCount / activeQuestCount`，無任務時為 0。
- `Threshold`：一般模式 0.70，困難模式 1.00。
- `IsCleared = CompletionRatio >= Threshold`。
- 今日首次變成 IsCleared 時：XpEvent(Source=DailyBonus, +30)，DailyLog.BonusGranted = true。若之後又變成未達標：XpEvent(Source=DailyBonusUndo, -30)，BonusGranted = false。
- 切換困難模式立即套用到今日的判定，並依上述規則補發或收回獎勵。

### 4.6 連續紀錄

- `Player.Streak` 只在結算時變動：已結算的日子 IsCleared → +1，否則歸零。
- API 回傳的 `displayStreak = Streak + (今日 IsCleared ? 1 : 0)`。
- `BestStreak = max(BestStreak, displayStreak)` 在結算與進度更新時維護。

### 4.7 困難模式懲罰

結算時若 `HardMode == true` 且該日 `!IsCleared`：`penalty = round(XpNeeded(Level) * 0.15)`（`MidpointRounding.AwayFromZero`），XpEvent(Source=Penalty, -penalty)，依 4.2 的懲罰規則扣除。同一次結算最多懲罰 3 天，之後的日子只歸零連續紀錄。

### 4.8 66 天計畫

- `Program`：`StartDate`、`Cycle`（從 1 起）、`LengthDays = 66`。
- `DayNumber = (Today - StartDate).Days + 1`；超過 66 時 API 回 `isCompleted = true`。
- 開新週期：`StartDate = Today`，`Cycle += 1`；不重置玩家等級與屬性。

### 4.9 任務的編輯與刪除

- 刪除 = `IsArchived = true`、`ArchivedAt = now`，不刪列；已有的 QuestProgress 與 DailyLog 不變。
- 修改任務類型時，清除該任務今日的 QuestProgress（含撤銷已發的 EXP 與屬性）。修改其他欄位不動今日進度。
- 達標率只計算 `IsArchived == false` 的任務。
- 封存任務後重算「今日」達標率（分母變小），依 4.5 補發或收回獎勵；歷史 DailyLog 的 CompletionRatio 不動。

### 4.10 新使用者預設任務

註冊成功後自動建立以下 9 個任務：

```csv
Name,StatType,Difficulty,QuestType,TargetValue,Step,Unit
23:30 前上床睡覺,VIT,Normal,Check,,,
7:30 前起床,VIT,Hard,Check,,,
喝水,VIT,Easy,Count,8,1,杯
閱讀,INT,Normal,Count,20,5,分鐘
技術學習 30 分鐘,INT,Normal,Check,,,
手機螢幕時間,WIL,Hard,Limit,3,0.5,小時
睡前 1 小時不滑手機,WIL,Hard,Check,,,
運動 30 分鐘,STR,Normal,Check,,,
寫下今天的三件好事,SPI,Easy,Check,,,
```

## 5. 資料模型（EF Core 實體）

所有主鍵為 `Guid`（`uuid`），皆有 `CreatedAt`（`bigint`，Unix 毫秒）。所有時間戳欄位（`CreatedAt`、`ArchivedAt`、`SettledAt`、`OccurredAt`）同此規則；欄位註解須標明單位。

| 實體 | 欄位（型別，約束） |
| --- | --- |
| User | Id；Email（citext，unique）；PasswordHash；DisplayName（≤ 40）；TimeZoneId（IANA，預設 `Asia/Taipei`） |
| Player | UserId（PK，FK User）；Level int ≥ 1；Xp int ≥ 0；Str/Vit/Int/Wil/Spi int ≥ 0；HardMode bool；Streak int；BestStreak int；TotalCompleted int；LastSettledDate DateOnly nullable |
| Program | Id；UserId（FK）；StartDate DateOnly；Cycle int；LengthDays int = 66；IsActive bool；每使用者同時只有一筆 IsActive = true（partial unique index） |
| Quest | Id；UserId（FK）；Name（≤ 60）；StatType enum；Difficulty enum；QuestType enum；TargetValue decimal(10,2) nullable；Step decimal(10,2) nullable；Unit（≤ 10）nullable；SortOrder int；IsArchived bool；ArchivedAt bigint nullable |
| DailyLog | Id；UserId（FK）；Date DateOnly；unique(UserId, Date)；CompletionRatio decimal(5,4)；IsCleared bool；BonusGranted bool；Note text nullable；IsSettled bool；SettledAt bigint nullable |
| QuestProgress | Id；DailyLogId（FK）；QuestId（FK）；unique(DailyLogId, QuestId)；Value decimal(10,2) nullable；IsDone bool；XpGranted int；StatGranted int |
| XpEvent | Id；UserId（FK）；Amount int（可負）；Source enum {Quest, QuestUndo, DailyBonus, DailyBonusUndo, Penalty}；RefId Guid nullable；OccurredAt bigint |

索引：`XpEvent(UserId, OccurredAt)`、`Quest(UserId, IsArchived)`、`DailyLog(UserId, Date)`。

## 6. API 規格

所有端點在 `/api/v1`，除 auth 外皆需 `Authorization: Bearer`。錯誤回應統一為 `{ "error": { "code": "string", "message": "string" } }`。所有需要「今日」的端點先執行結算（第 7 節）。時間戳欄位一律 Unix 秒；日期欄位一律 `YYYY-MM-DD`。

| 方法與路徑 | 用途 | 請求 | 回應 |
| --- | --- | --- | --- |
| POST /auth/register | 註冊 | `{email, password, displayName, timeZoneId?}` | 201 `{token, user}`；建立 Player、Program、預設任務 |
| POST /auth/login | 登入 | `{email, password}` | 200 `{token, user}`；失敗 401 |
| GET /me | 玩家總覽 | — | `{user:{id, email, displayName, timeZoneId}, player:{level, xp, xpNeeded, rank, title, stats:{str,vit,int,wil,spi}, hardMode, displayStreak, bestStreak, totalCompleted}, program:{startDate, cycle, dayNumber, lengthDays, isCompleted}}` |
| PATCH /me | 更新設定 | `{displayName?, timeZoneId?, hardMode?}` | 200 同 GET /me；hardMode 變更觸發 4.5 |
| GET /quests | 任務清單 | — | `[{id, name, statType, difficulty, questType, targetValue, step, unit, sortOrder}]`，不含已封存 |
| POST /quests | 新增 | `{name, statType, difficulty, questType, targetValue?, step?, unit?}` | 201 任務；Count/Limit 缺 targetValue 回 400 |
| PUT /quests/{id} | 修改 | 同 POST | 200 任務；套用 4.9 |
| DELETE /quests/{id} | 封存 | — | 204 |
| PUT /quests/reorder | 排序 | `{questIds:[...]}` | 204 |
| GET /today | 今日任務與進度 | — | `{date, completionRatio, isCleared, threshold, bonusGranted, note, quests:[{...任務欄位, value, isDone, xpReward, statReward}]}` |
| PUT /today/quests/{id}/progress | 寫入進度 | `{value: number 或 null}` | 200 同 GET /today；Check 類型只接受 0 或 1；Count 負值回 400 |
| PUT /today/note | 今日反思 | `{note}`（≤ 2000 字） | 204 |
| GET /history?from=YYYY-MM-DD&to=YYYY-MM-DD | 每日紀錄 | 區間 ≤ 100 天 | `[{date, completionRatio, isCleared, doneQuestIds:[...], note}]`，含今日的即時值 |
| GET /xp-events?limit=50 | EXP 流水 | — | `[{amount, source, refId, occurredAt}]`，新到舊 |
| POST /program/restart | 開新 66 天 | — | 200 program |

## 7. 結算演算法

```
Settle(userId, now):
  today = ToUserDate(now, user.TimeZoneId)
  取得 Player 列並加鎖（SELECT ... FOR UPDATE）
  start = player.LastSettledDate == null ? ToUserDate(player.CreatedAt) : player.LastSettledDate + 1
  penaltiesApplied = 0
  for date in [start, today):            // 不含 today；start >= today 時迴圈為空
      log = DailyLog(userId, date) 或新建 { CompletionRatio = 0, IsCleared = false }
      if !log.IsSettled:
          // CompletionRatio 沿用 log 現值：進度只能寫「今日」，日終時該值即為當日最終達標率
          log.IsCleared = log.CompletionRatio >= Threshold(player.HardMode)
          if log.IsCleared: player.Streak += 1
          else:
              player.Streak = 0
              if player.HardMode && penaltiesApplied < 3:
                  penalty = round(XpNeeded(player.Level) * 0.15)
                  加入 XpEvent(Penalty, -penalty, RefId = log.Id)
                  player.Xp = max(0, player.Xp - penalty)
                  penaltiesApplied += 1
          player.BestStreak = max(player.BestStreak, player.Streak)
          log.IsSettled = true; log.SettledAt = now
  player.LastSettledDate = max(player.LastSettledDate, today - 1)   // 改時區往西時不倒退、不重跑
  確保 DailyLog(userId, today) 存在（IsSettled = false）
  commit
```

要求：整段在一個交易內；同一使用者併發呼叫時第二個要等第一個完成（靠列鎖）；連續缺席 400 天以上時，只逐日結算最近 400 天，更早的日子一次性歸零連續紀錄。另提供 `IHostedService` 每小時對 `LastSettledDate < 各使用者今日 - 1` 的使用者執行 Settle。

## 8. 前端（第一階段）

只做四個畫面，行動優先，純 HTML + JS，放在 `src/SoloLeveling.Api/wwwroot`：

1. 登入／註冊。
2. 今日：頂部顯示 Level、EXP 進度條、階級與稱號、displayStreak、今日完成度；下方依屬性分組列任務：Check 顯示勾選框，Count 顯示 −/＋ 與 `value/target unit`，Limit 顯示數字輸入框。每次操作呼叫 PUT progress 並以回應覆蓋畫面。
3. 進度：66 格日曆（GET /history 自 program.startDate 起 66 天），五維屬性數值，最近 7 天各任務完成點。
4. 設定：困難模式開關、任務新增／編輯／封存、開新 66 天。

## 9. 測試要求（最少）

領域層單元測試：

- 等級：Lv1 得 100 EXP → Lv2、Xp 0；一次得 250 EXP 從 Lv1 → Lv3、Xp 30；撤銷造成降級；Lv1 撤銷不低於 0。
- 完成判定三種類型，含 Limit 的 null、等於目標、超過目標。
- 進度更新：完成→撤銷 EXP 與屬性淨變化為 0；達標獎勵只發一次；達標後撤銷收回獎勵。
- 模式切換：一般 70% 達標；切到困難後同一天變未達標並收回獎勵。

結算整合測試（Testcontainers）：

- 跨一天：昨日達標 → Streak 1；未達標 → Streak 0。
- 缺席 5 天：Streak 歸零，困難模式只扣 3 次懲罰。
- 時區：`Asia/Taipei` 使用者在 UTC 15:59 與 16:01 的「今日」不同。
- 併發：兩個請求同時觸發結算，XpEvent 中 Penalty 只出現一次。
- 封存任務不影響歷史 DailyLog 的 CompletionRatio。

## 10. 實作順序

1. 建 solution、專案、Docker Compose、EF Core 實體與第一版 migration。
2. Domain 層：常數、`LevelingService`、`CompletionRules`、`ProgressUpdater`，配單元測試。
3. `SettlementService` 與整合測試。
4. Auth 與 `/me`、`/quests`。
5. `/today` 系列與 `/history`、`/xp-events`、`/program/restart`。
6. HostedService 排程結算。
7. 前端四個畫面。
8. README：啟動方式、環境變數、API 範例 curl。

## 11. 驗收清單

- [ ] `docker compose up` 後可註冊、登入、看到 9 個預設任務。
- [ ] 勾完 7 個任務（70%）在一般模式看到 isCleared = true 與 +30 EXP 事件。
- [ ] 把系統時間往後撥一天再呼叫 GET /today，前一天出現在 /history 且 displayStreak 正確。
- [ ] 困難模式下漏一天，XpEvent 出現一筆 Penalty，Level 不變。
- [ ] 所有測試綠燈；`dotnet format` 無警告。

## 12. 禁止事項

- 不要在客戶端計算 EXP 或等級，一律由 API 回傳。
- 不要直接修改 Player.Xp 而不寫 XpEvent。
- 不要硬刪 Quest 或 DailyLog。
- 不要用本機時間或伺服器時區判斷「今日」。
- 不要新增本文未列的功能；有想法寫進 README 的「後續」段落。

## 13. 開發流程

- 目前直接 commit 到 `main`。
- 此 repo 的 git 身分為個人帳號（`WinnixShih`），由 `~/.gitconfig` 的 `includeIf gitdir:D:/Github/` 自動套用，不用手動設定。
