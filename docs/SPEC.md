# SoloLeveling 實作規格：習慣養成 App（MVP 後端）

本文件是實作用的規格。所有數值、規則、API 形狀都已決定，實作者不需要再做產品判斷；遇到本文未定義的細節，選最簡單可運作的做法並在 README 記錄。第一階段只做後端與可用的 Web 前端，手機 App 之後再說。

## 1. 交付範圍

必須完成：

- ASP.NET Core 10 Web API（Controllers），含 EF Core + PostgreSQL、JWT 驗證、Swagger。
- 第 4 節所有領域規則與第 7 節結算演算法，並有單元測試與整合測試。
- 第 6 節所有端點。
- 一個簡單的 Web 前端（純 HTML + JS，由 API 的 `wwwroot` 靜態提供）能完成每日操作流程：登入、看今日任務、勾選、累計、填上限值、看等級與屬性、看 66 天日曆。
- Docker Compose 一鍵啟動（api + postgres）。
- 註冊後由引導流程依固定類別產生漸進式任務。

不做：推播、好友、排名、專注計時器、iOS/Android 原生 App、付費、AI 產生計畫。獎勵系統（寶箱、卡片、金幣、連勝保險、稱號、主題）見 4.12。

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

- `Player.Streak` 只在結算時變動：已結算的日子 IsCleared → +1，否則歸零（連勝進行中且持有保險卡時改為消耗 1 張並 +1，見 4.12 與第 7 節）。
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

### 4.10 基本任務（原預設任務）

以下 9 個任務可在引導流程最後一步勾選加入，被目標所取代的任務無法勾選：

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

### 4.11 引導式目標與漸進任務

新帳號註冊後進入引導流程，依使用者的選擇與現況產生多個目標，每個目標包含若干漸進式任務，目標值會隨著達標天數逐步逼近終點。

**目標類別與取代關係**

| 類別 | 名稱 | 問題 | 產生任務 | 屬性／難度 | 取代基本任務索引 |
| --- | --- | --- | --- | --- | --- |
| `Routine` | 作息 | 現在就寢時間、目標就寢時間、現在起床時間、目標起床時間、天數 | `{target} 前上床睡覺`（Check）、`{target} 前起床`（Check） | VIT／Normal、VIT／Hard | 0、1 |
| `Exercise` | 運動 | 現在分鐘、目標分鐘、天數 | `運動`（Count，單位分鐘，Step 5） | STR／Normal | 7 |
| `Reading` | 閱讀 | 現在分鐘、目標分鐘、天數 | `閱讀`（Count，單位分鐘，Step 5） | INT／Normal | 3 |
| `ScreenTime` | 螢幕時間 | 現在小時、目標小時、天數 | `手機螢幕時間`（Limit，單位小時，Step 0.5） | WIL／Hard | 5 |

天數 `lengthDays` 介於 7 到 90；時間格式 `HH:MM`（00:00 到 23:59）；目標必須比現況「更好」（就寢與起床目標不晚於現況；運動、閱讀、螢幕時間目標分別更多、更多、更少），允許相等。

**時間編碼**

時間點存為「距基準時刻的分鐘數」，0 到 1439。就寢時間以中午 12:00 為基準：例 01:00 是 780、23:30 是 690。起床時間以 18:00 為基準（涵蓋 18:00 到隔天 17:59，早起即數值變小，避免中午後的起床時間被誤判成比現況差）：例 07:00 是 780、12:30 是 1110。顯示時轉換回 `HH:MM`：`((minutes / 60) + baseHour) % 24`，`baseHour` 就寢為 12、起床為 18。

**階段公式**

每個漸進任務有：`StartValue`（現況）、`EndValue`（終點）、`StepValue`（每階變化）、`StageCount`（總階）、`DaysPerStep`（每階天數，每階至少 3 天）。

建立時（`diff = |EndValue - StartValue|`，`granularity` 為類別的四捨五入單位）：
- `diff == 0`：`StageCount = 1`、`DaysPerStep = 3`、`StepValue = 0`。
- 否則：
  - `maxStages = ceil(diff / granularity)`：刻度允許的最多階數，刻度不夠細時用來限制階數上限。
  - `StageCount = max(1, min(ceil(lengthDays / 3), maxStages))`。
  - `DaysPerStep = max(3, ceil(lengthDays / StageCount))`：刻度不夠細、階數因此減少時，每階天數同步拉長。
  - `StepValue = (EndValue - StartValue) / StageCount`，按 granularity 四捨五入：時間 5 分鐘、分鐘 1、小時 0.25。四捨五入後為 0 但起終點不同時，取一個 granularity 的量並帶正確符號。
- 注意：表中「Step」指前端 UI 增減量（`Quest.Step`），與 `StepValue` 計算時的 granularity 不同。Granularity 是 `StepValue` 四捨五入的精度，UI Step 是使用者在前端勾選時的增減單位；運動、閱讀 UI Step 為 5 分鐘，螢幕時間 UI Step 為 0.5 小時。

每天的計算：
- `doneDays` = 該任務在**今天之前**所有 `QuestProgress.IsDone == true` 的天數，由 `TodayContextLoader` 一次批次載入所有漸進任務的 `DoneDaysBeforeToday`。
- `Stage = min(StageCount, 1 + floor(doneDays / DaysPerStep))`。第 1 天就是第 1 階，目標已經比現況好一階。
- `EffectiveTarget = Stage < StageCount ? StartValue + Stage × StepValue : EndValue`。最後一階固定等於終點。今天自己的完成狀態不影響今天的目標，只影響明天。

判定與顯示：
- Check 型：`Name` 存樣板含 `{target}` 佔位，API 出口用 `TimeOfDay.Format(EffectiveTarget)` 替換。
- Count／Limit 型：`EffectiveTarget` 當 `targetValue`，`Name` 不含佔位。
- 一律附 `progression` 物件含 `goalId`、`stage`、`stageCount`、`targetLabel`。
- 判定（是否達標）與完成率計算一律用 `EffectiveTarget` 而非 `quest.TargetValue`；一般任務的 `EffectiveTarget` 就是 `TargetValue`。
- `QuestProgress.TargetSnapshot` 記錄當天判定用的目標值。

**目標封存**

目標的 `IsArchived` 為 true 時連帶封存其未封存的任務；已結算的日子的達標率不變。漸進任務的目標欄位（`targetValue` 等）不可編輯，要改就封存目標重建。

### 4.12 獎勵系統（寶箱、卡片、金幣、連勝保險、稱號、主題）

獎勵只跟努力綁定，不能用金幣換 EXP、等級或連勝天數。

**寶箱**（建立時未開啟，使用者手動開）

| 觸發 | 寶箱 | 判定 |
| --- | --- | --- |
| 每升 1 級 | E | 只對超過 `Player.PeakLevel` 的等級發，撤銷降級後再升回來不重發 |
| 階級晉升 | C，另送 1 張保險卡（上限 3） | 新等級與前一級的 `RankOf` 不同 |
| 最佳連續達 7 天 | C | 成就「不屈」首次解鎖時 |
| 最佳連續達 30 天 | A | 成就「恆心」首次解鎖時 |
| 目標完成 | A | 目標每個未封存任務的達標天數（含今天）≥ `(StageCount − 1) × DaysPerStep + 1`，且目標期間（`LengthDays`）已走完（今天 − `StartDate` + 1 ≥ `LengthDays`）；本版之前建立、已符合條件的目標，會在使用者下一次請求時補發 A 箱；寫入 `Goal.CompletedAt` 後不再判定 |
| 66 天週期完成 | S | `POST /program/restart` 時 `today − StartDate ≥ LengthDays`，寫入 `Program.CompletedAt` |
| 金幣購買 | E | 200 金幣 |

**開箱**：從該等級全部卡片均勻抽一張（含已擁有）；未擁有加入收藏，已擁有張數 +1 並轉成重複金幣；另附開箱金幣。E 20／30、C 50／80、A 100／200、S 300／500（開箱／重複）。亂數來源注入。

**卡片**：目錄在 `Cards.All`（E 10、C 8、A 5、S 2），`Image` 為 `cards/{id}.webp`；插畫規格 2:3、768×1152、WebP 或 PNG、不含邊框與文字，檔案不存在時前端顯示佔位卡。

**金幣**（每次變動一筆 `CoinEvent`，經 `Wallet`）：今日首次達標 +10（`DailyLog.ClearCoinsGranted` 跟著 `BonusGranted`；退回達標時收回，最多扣到 0）；開箱與重複卡見上；解鎖成就 +50。商店：保險卡 100（最多持有 3）、E 級寶箱 200、主題紫影／翡翠各 500（藍光免費預設）。錯誤碼 `NotEnoughCoins`、`ShieldLimitReached`、`ThemeOwned`、`UnknownTheme`（皆 400）。

**連勝保險卡**：結算時某天未達標、`Streak > 0` 且 `ShieldCount > 0` → 消耗 1 張、Streak +1、記一筆 `RewardEvent(ShieldUsed, date)`；困難模式懲罰照扣；補多天時逐日判定。事件在下一個套用獎勵的請求回報一次（`AnnouncedAt`）。

**成就與稱號**：目錄在 `Achievements.All`（12 個），條件成立且未解鎖即解鎖（唯一主鍵擋重複），+50 金幣，解鎖一個字塊。任務角色由漸進任務推導：作息＋`TimeOfDay` → 就寢、作息＋`TimeOfDayEvening` → 起床、運動／閱讀／螢幕時間同名。分類連續天數＝未封存角色任務「到昨天為止連續完成天數，今天完成再 +1」；分類累計分鐘＝運動、閱讀任務（含已封存）所有進度值加總。稱號：`Player.TitlePrefixKey`／`TitleSuffixKey` 只能選已解鎖且槽位正確的字塊；有選 → 「前綴・後綴」（只選一邊顯示該邊），都沒選 → 階級稱號。

**主題**：`Player.ThemeKey`（`azure`／`violet`／`jade`，預設 `azure`），已購買記在 `OwnedThemes`。

**判定時機**：所有會改玩家狀態的端點在第一次 `SaveChanges` 之後呼叫一次 `RewardApplier`（內部執行 `Rewards.Evaluate`），結果寫入 DB 並放進回應的 `rewards` 欄位；`GET /today`、`GET /me` 只在本次請求新結算了至少一天、或有待公告的保險卡事件時才判定，否則回空的 `rewards`。排程結算不發寶箱，等下一次判定的請求補發。`GET /rewards` 只結算、不套用獎勵（套用會吃掉待公告的保險卡事件）；`GET /cards`、`GET /goals`、`GET /history` 不套用獎勵。

**前端**：導覽第三格「檔案（HUNTER）」呈現狀態面板與稱號組合、金幣與保險卡、待開寶箱、成就、圖鑑與釘選卡；商店視窗有保險卡與 E 級寶箱兩項，主題在設定頁購買與切換。系統訊息依 `rewards` 依序顯示：升級 → 晉階 → 寶箱 → 成就 → 保險卡生效。

## 5. 資料模型（EF Core 實體）

所有主鍵為 `Guid`（`uuid`），皆有 `CreatedAt`（`bigint`，Unix 毫秒）。所有時間戳欄位（`CreatedAt`、`ArchivedAt`、`SettledAt`、`OccurredAt`）同此規則；欄位註解須標明單位。

| 實體 | 欄位（型別，約束） |
| --- | --- |
| User | Id；Email（citext，unique）；PasswordHash；DisplayName（≤ 40）；TimeZoneId（IANA，預設 `Asia/Taipei`） |
| Player | UserId（PK，FK User）；Level int ≥ 1；Xp int ≥ 0；Str/Vit/Int/Wil/Spi int ≥ 0；HardMode bool；Streak int；BestStreak int；TotalCompleted int；LastSettledDate DateOnly nullable；PeakLevel int（曾到達的最高等級）；Coins int ≥ 0；ShieldCount int 0–3；ThemeKey（≤ 16，預設 azure）；TitlePrefixKey／TitleSuffixKey（≤ 32）nullable；PinnedCardId（≤ 64）nullable |
| Program | Id；UserId（FK）；StartDate DateOnly；Cycle int；LengthDays int = 66；IsActive bool；每使用者同時只有一筆 IsActive = true（partial unique index）；CompletedAt bigint nullable |
| Goal | Id；UserId（FK）；Category enum（`Routine`／`Exercise`／`Reading`／`ScreenTime`）；Answers text（JSON）；LengthDays int；StartDate DateOnly；IsArchived bool；ArchivedAt bigint nullable；CompletedAt bigint nullable；CreatedAt bigint |
| Quest | Id；UserId（FK）；GoalId Guid nullable（FK Goal，漸進任務才有值）；Name（≤ 60）；StatType enum；Difficulty enum；QuestType enum；TargetValue decimal(10,2) nullable；Step decimal(10,2) nullable；Unit（≤ 10）nullable；ValueKind enum nullable（`Number`／`TimeOfDay`／`TimeOfDayEvening`，漸進任務才有值；`TimeOfDayEvening` 供起床時間使用，編碼基準為 18:00）；StartValue decimal(10,2) nullable；EndValue decimal(10,2) nullable；StepValue decimal(10,2) nullable；StageCount int nullable；DaysPerStep int nullable；SortOrder int；IsArchived bool；ArchivedAt bigint nullable |
| DailyLog | Id；UserId（FK）；Date DateOnly；unique(UserId, Date)；CompletionRatio decimal(5,4)；IsCleared bool；BonusGranted bool；ClearCoinsGranted bool；Note text nullable；IsSettled bool；SettledAt bigint nullable |
| QuestProgress | Id；DailyLogId（FK）；QuestId（FK）；unique(DailyLogId, QuestId)；Value decimal(10,2) nullable；IsDone bool；XpGranted int；StatGranted int；TargetSnapshot decimal(10,2) nullable |
| XpEvent | Id；UserId（FK）；Amount int（可負）；Source enum {Quest, QuestUndo, DailyBonus, DailyBonusUndo, Penalty}；RefId Guid nullable；OccurredAt bigint |
| RewardChest | Id；UserId（FK）；Rarity enum（E／C／A／S）；Source enum {LevelUp, RankUp, Streak7, Streak30, GoalCompleted, ProgramCompleted, Purchase}；CreatedAt bigint；OpenedAt bigint nullable；DroppedCardId（≤ 64）nullable；Coins int |
| OwnedCard | PK (UserId, CardId)；Count int ≥ 1；FirstAcquiredAt bigint |
| Achievement | PK (UserId, Key)；UnlockedAt bigint |
| OwnedTheme | PK (UserId, ThemeKey) |
| CoinEvent | Id；Seq bigint identity；UserId（FK）；Amount int（可負）；Source enum {DailyClear, DailyClearUndo, ChestOpen, DuplicateCard, Achievement, ShopShield, ShopChest, ShopTheme}；RefId Guid nullable；OccurredAt bigint |
| RewardEvent | Id；UserId（FK）；Kind enum {ShieldUsed}；Date DateOnly；OccurredAt bigint；AnnouncedAt bigint nullable |

索引：`XpEvent(UserId, OccurredAt)`、`Quest(UserId, IsArchived)`、`DailyLog(UserId, Date)`、`Goal(UserId, IsArchived)`、`Goal` 部分唯一索引 `(UserId, Category) WHERE IsArchived = false`、`RewardChest(UserId, OpenedAt)`、`CoinEvent(UserId, OccurredAt)`、`RewardEvent(UserId, AnnouncedAt)`。

## 6. API 規格

所有端點在 `/api/v1`，除 auth 外皆需 `Authorization: Bearer`。錯誤回應統一為 `{ "error": { "code": "string", "message": "string" } }`。所有需要「今日」的端點先執行結算（第 7 節）。時間戳欄位一律 Unix 秒；日期欄位一律 `YYYY-MM-DD`。

| 方法與路徑 | 用途 | 請求 | 回應 |
| --- | --- | --- | --- |
| POST /auth/register | 註冊 | `{email, password, displayName, timeZoneId?}` | 201 `{token, user}`；建立 Player、Program；不再建立預設任務 |
| POST /auth/login | 登入 | `{email, password}` | 200 `{token, user}`；失敗 401 |
| GET /me | 玩家總覽 | — | `{user:{id, email, displayName, timeZoneId}, player:{level, xp, xpNeeded, rank, title, stats:{str,vit,int,wil,spi}, hardMode, displayStreak, bestStreak, totalCompleted, rankTitle, coins, shieldCount, themeKey, pinnedCard}, program:{startDate, cycle, dayNumber, lengthDays, isCompleted}, needsOnboarding}`；title 為組合後稱號，rewards 見表後說明 |
| PATCH /me | 更新設定 | `{displayName?, timeZoneId?, hardMode?}` | 200 同 GET /me；hardMode 變更觸發 4.5 |
| GET /goals/categories | 目標類別定義 | — | 類別表與基本任務清單，前端畫表單用；`questions[].type` 為 `Time`／`Integer`／`Decimal` |
| POST /goals/preview | 預覽目標產生的任務 | `{goals:[{category, answers}], basicQuestIndexes?}` | 不寫入，回每個目標的任務與階段摘要 |
| POST /goals | 建立目標與任務 | `{goals:[{category, answers}], basicQuestIndexes?}` | 201；回 `GET /goals` 格式，其他欄位或同類別已有進行中的回 400/409 |
| GET /goals | 目標清單 | — | `{goals:[{id, category, title, lengthDays, startDate, quests:[{id, name, stage, stageCount, targetLabel, isArchived}]}]}` |
| DELETE /goals/{id} | 封存目標與其任務 | — | 200 `{rewards}`；重算今日達標率 |
| GET /quests | 任務清單 | — | `[{id, name, statType, difficulty, questType, targetValue, step, unit, sortOrder, goalId?}]`，不含已封存 |
| POST /quests | 新增 | `{name, statType, difficulty, questType, targetValue?, step?, unit?}` | 201 任務；Count/Limit 缺 targetValue 回 400 |
| PUT /quests/{id} | 修改 | 同 POST | 200 任務；漸進任務只允許改 statType、difficulty，其他回 400 `ProgressionQuestLocked`；套用 4.9 |
| DELETE /quests/{id} | 封存 | — | 200 `{rewards}`；漸進任務可單獨封存 |
| PUT /quests/reorder | 排序 | `{questIds:[...]}` | 204 |
| GET /today | 今日任務與進度 | — | `{date, completionRatio, isCleared, threshold, bonusGranted, note, quests:[{...任務欄位, value, isDone, xpReward, statReward, progression?}]}` |
| PUT /today/quests/{id}/progress | 寫入進度 | `{value: number 或 null}` | 200 同 GET /today；Check 類型只接受 0 或 1；Count 負值回 400 |
| PUT /today/note | 今日反思 | `{note}`（≤ 2000 字） | 204 |
| GET /history?from=YYYY-MM-DD&to=YYYY-MM-DD | 每日紀錄 | 區間 ≤ 100 天 | `[{date, completionRatio, isCleared, doneQuestIds:[...], note}]`，含今日的即時值 |
| GET /xp-events?limit=50 | EXP 流水 | — | `[{amount, source, refId, occurredAt}]`，新到舊 |
| POST /program/restart | 開新 66 天 | — | 200 program（`rewards` 在 program 物件內） |
| GET /rewards | 獎勵總覽 | — | `{coins, shieldCount, unopenedChests, achievements:[{key, name, condition, titleText, slot, unlocked, progress, target, unlockedAt}], titleFragments, titlePrefixKey, titleSuffixKey, title, pinnedCard, themeKey, ownedThemes}` |
| POST /rewards/chests/{id}/open | 開箱 | — | `{card, isDuplicate, coins, coinBalance, rewards}`；已開或別人的寶箱 404 `ChestNotFound` |
| GET /cards | 圖鑑 | — | `{cards:[{id, name, rarity, flavor, image, owned, count, firstAcquiredAt}], ownedKinds, total}` |
| POST /shop/purchase | 商店 | `{item: "Shield"｜"EChest"｜"Theme", themeKey?}` | `{coins, shieldCount, ownedThemes, chest, rewards}`；400 `NotEnoughCoins`／`ShieldLimitReached`／`ThemeOwned`／`UnknownTheme` |
| PUT /me/title | 稱號組合 | `{prefixKey, suffixKey}` | 200 同 GET /me；400 `TitleNotUnlocked` |
| PUT /me/pinned-card | 釘選卡 | `{cardId｜null}` | 200 同 GET /me；400 `CardNotOwned` |
| PUT /me/theme | 切換主題 | `{themeKey}` | 200 同 GET /me；400 `ThemeNotOwned` |

`GET /today`、`PUT /today/quests/{id}/progress`、`POST/PUT /quests`、`DELETE /quests/{id}`、`POST /goals`、`DELETE /goals/{id}`、`GET/PATCH /me`、`PUT /me/*`、`POST /program/restart`（`rewards` 在回應的 `program` 物件內）的回應帶 `rewards`：`{levelsGained, rankUps, newChests, newAchievements, coinDelta, shieldsGained, shieldsUsed, shieldCount}`；`POST /rewards/chests/{id}/open` 與 `POST /shop/purchase` 的回應也帶；清單與 `GET /goals` 不帶此欄位。

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
              if player.ShieldCount > 0 && player.Streak > 0:
                  player.ShieldCount -= 1; player.Streak += 1
                  加入 RewardEvent(ShieldUsed, date)        // 下一個套用獎勵的請求回報
              else:
                  player.Streak = 0
              if player.HardMode && penaltiesApplied < 3:   // 用了保險卡也照扣
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

做六個畫面，行動優先，純 HTML + JS，放在 `src/SoloLeveling.Api/wwwroot`：

1. 登入／註冊。
2. 引導（新帳號）：三步流程，(1) 勾選要建立的目標類別（至少一個、多個且同類別最多一個）；(2) 逐類別填表，表單依 GET /goals/categories 產生，`time` 用 `<input type="time">`；(3) 預覽呼叫 POST /goals/preview，顯示每個任務的起終點、階數、每階變化，下方列基本任務勾選清單（被取代的預設不勾且停用）。確認呼叫 POST /goals 後進入今日畫面。
3. 今日：漸進任務名字含當天 EffectiveTarget，右側小字「第 1／10 階」；其他不變。
4. 進度：66 格日曆（GET /history 自 program.startDate 起 66 天），五維屬性數值，最近 7 天各任務完成點。
5. 檔案（HUNTER）：狀態面板與稱號組合、金幣與保險卡、待開寶箱、成就、圖鑑與釘選卡；商店視窗賣保險卡與 E 級寶箱，另有前往設定頁買主題的連結。
6. 設定：困難模式開關、任務新增／編輯（漸進任務不在一般任務清單中，目標區塊只提供封存）；新增「目標」區塊列未封存目標（含類別、開始日、各任務階段），每個目標有「封存」（二次確認）；「新增目標」按鈕進只做一個類別的引導流程（類別選單排除已有進行中的）；開新 66 天。

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

- [ ] `docker compose up` 後可註冊、登入，新帳號進入引導流程；選作息與閱讀、填現況與目標、預覽看到階數摘要、確認後今日頁出現任務且名字帶第 1 階時間。
- [ ] 連續勾完成 3 天後，第 4 天就寢任務的時間提早；某天不勾，隔天時間不變。
- [ ] 設定頁看得到目標與階段，封存目標後任務消失。
- [ ] 既有帳號（已有任務者）登入行為不變，無需引導。
- [ ] 勾完 7 個任務（70%）在一般模式看到 isCleared = true 與 +30 EXP 事件。
- [ ] 把系統時間往後撥一天再呼叫 GET /today，前一天出現在 /history 且 displayStreak 正確。
- [ ] 困難模式下漏一天，XpEvent 出現一筆 Penalty，Level 不變。
- [ ] 新帳號從 Lv.1 升到 Lv.2 時看到「等級提升」與「獲得 E 級寶箱」兩則系統訊息，檔案頁可開箱並在圖鑑看到卡片。
- [ ] 漏一天且持有保險卡時，隔天連勝不中斷並看到保險生效訊息。
- [ ] 用金幣買到紫影後可切換，介面整體換色。
- [ ] 稱號可組合成「靜夜的・百戰獵人」並顯示在狀態面板。
- [ ] 所有測試綠燈；`dotnet format` 無警告。

## 12. 禁止事項

- 不要在客戶端計算 EXP 或等級，一律由 API 回傳。
- 不要直接修改 Player.Xp 而不寫 XpEvent。
- 不要硬刪 Quest 或 DailyLog。
- 不要用本機時間或伺服器時區判斷「今日」。
- 不要新增本文未列的功能（已列於 4.12 的除外）；有想法寫進 README 的「後續」段落。

## 13. 開發流程

- 目前直接 commit 到 `main`。
- 此 repo 的 git 身分為個人帳號（`WinnixShih`），由 `~/.gitconfig` 的 `includeIf gitdir:D:/Github/` 自動套用，不用手動設定。
