# 獎勵系統：寶箱、卡片圖鑑、金幣、連勝保險、稱號 設計

## 目標

讓努力有看得見、拿得到、能炫耀的回報：達成里程碑得到寶箱，開出收藏卡片與金幣；金幣能換連勝保險卡、寶箱與主題色；成就解鎖稱號字塊，自由組成「前綴＋後綴」顯示在個人檔案。獎勵只跟努力綁定，不能用來買 EXP、等級或連勝天數。

前置：`docs/superpowers/specs/2026-10-01-system-ui-redesign-design.md`（系統介面改版）完成後實作；本系統的畫面一律使用該規格的元件與色票。

## 背景與決定

- 不做角色紙娃娃與使用者自訂現實獎勵；收藏品是**卡片**（整張插畫，無圖層對齊問題）。
- 卡片插畫由作者用 AI 另外產生，規格見第 3 節；程式先以佔位卡（色塊＋名稱）運作。
- 成就不給卡片，給**稱號字塊**。
- 主題色：**藍光預設免費**，紫影與翡翠各 500 金幣解鎖。
- 連勝保險卡**自動生效**。
- 卡片稀有度與寶箱等級**一對一**；抽卡可能重複，重複轉成金幣。

## 設計

### 1. 寶箱

| 觸發 | 寶箱 | 判定 |
| --- | --- | --- |
| 每升 1 級 | E | 只對超過 `Player.PeakLevel` 的等級發，撤銷降級後再升回來不重發 |
| 階級晉升（E→D…→S） | C | 前後 `Leveling.RankOf` 不同；另送 1 張保險卡 |
| 最佳連續達 7 天 | C | `BestStreak` 首次跨過 7 |
| 最佳連續達 30 天 | A | `BestStreak` 首次跨過 30 |
| 目標完成 | A | 目標所有未封存任務都到最後一階，`Goal.CompletedAt` 由 null 變有值 |
| 66 天週期完成 | S | 開新週期時舊週期已滿 66 天，`Program.CompletedAt` 寫入 |
| 金幣購買 | E | 200 金幣 |

- 寶箱建立後為「未開啟」，使用者手動開。
- 「首次跨過」以既有的一次性紀錄防重複：連續 7／30 天由對應成就（第 5 節）同時判定，已解鎖就不再發。
- 排程結算不發獎勵；等使用者下一次請求時由該請求補判定（成就以狀態判定，升級以 `PeakLevel` 判定）。

### 2. 開箱

- 從該級全部卡片中**均勻抽一張**（含已擁有）。未擁有 → 加入收藏；已擁有 → 張數 +1，並轉成重複金幣。
- 另附固定金幣。

| 等級 | 開箱金幣 | 重複卡金幣 |
| --- | --- | --- |
| E | 20 | 30 |
| C | 50 | 80 |
| A | 100 | 200 |
| S | 300 | 500 |

- 亂數來源注入（測試可固定）。

### 3. 卡片

- 目錄定義在 Domain 常數清單 `Cards.All`：`Id`（kebab-case）、`Name`、`Rarity`（E／C／A／S）、`Flavor`（一兩句風味文字）、`Image`（`cards/{id}.webp`）。第一批建議 E 10、C 8、A 5、S 2，共 25 張。
- 圖檔放 `wwwroot/cards/`；檔案不存在時前端顯示佔位卡（稀有度色塊＋名稱），所以目錄可先於插畫存在。
- **插畫規格**（給作者產圖）：只畫畫面本體，不含邊框與文字；2:3、768×1152；WebP 或 PNG；檔名即 `Id`。邊框、稀有度色、名稱、風味文字由前端疊上，讓不同批次的插畫被統一卡框收住。主題建議：獵人裝備、地下城場景、影子、系統介面、藥水與鑰匙，S 級留給最有戲劇性的畫面。
- **圖鑑**：網格顯示全部卡片；未擁有為剪影加「？」，已擁有顯示插畫、張數；點開看大圖、稀有度、風味文字、首次取得日。
- **釘選展示卡**：從已擁有中選一張，顯示在檔案頁頂端與狀態面板名稱旁（小圖）。可取消。

### 4. 金幣與商店

| 來源 | 數量 |
| --- | --- |
| 每日達標（首次達標當下，退回達標時收回） | +10 |
| 開箱 | 見第 2 節 |
| 重複卡 | 見第 2 節 |
| 解鎖成就 | +50 |

| 商品 | 價格 | 限制 |
| --- | --- | --- |
| 連勝保險卡 | 100 | 最多持有 3 張 |
| E 級寶箱 | 200 | 無 |
| 主題：紫影 | 500 | 一次性 |
| 主題：翡翠 | 500 | 一次性 |

- 每筆金幣變動記一筆 `CoinEvent`（同 XpEvent 精神：Player.Coins 的變動都要有對應事件）。
- 金幣不足、保險卡已滿、主題已擁有 → 400，錯誤碼 `NotEnoughCoins`、`ShieldLimitReached`、`ThemeOwned`。

### 5. 連勝保險卡

- `Player.ShieldCount`，上限 3。來源：商店、階級晉升。
- **結算時自動生效**：某一天未達標且 `ShieldCount > 0` → 消耗 1 張、`Streak` 視同延續（+1），記一筆 `RewardEvent(ShieldUsed, date)`。困難模式的 EXP 懲罰照扣。
- 一次結算補多天時，依日期逐日判定，保險卡用完為止。
- 前端在下一次請求的 `rewards` 回應中顯示「[系統] 連勝保險已生效，剩餘 N 張」。

### 6. 成就與稱號

- 成就定義在 Domain 常數清單 `Achievements.All`：`Key`、`Name`、條件、解鎖的字塊（`Text` 與 `Slot` 前綴／後綴）。解鎖一次性，唯一索引擋重複，解鎖時 +50 金幣。
- 第一批：

| 成就 | 條件 | 字塊 |
| --- | --- | --- |
| 初次覺醒 | 建立第一個目標 | 前綴「覺醒的」 |
| 不屈 | 最佳連續 7 天 | 前綴「不屈的」 |
| 恆心 | 最佳連續 30 天 | 前綴「恆心的」 |
| 百戰 | 累計完成 100 個任務 | 後綴「百戰獵人」 |
| 晨曦 | 起床任務連續達標 30 天 | 後綴「晨曦騎士」 |
| 靜夜 | 就寢任務連續達標 30 天 | 前綴「靜夜的」 |
| 手機克星 | 螢幕時間任務連續達標 30 天 | 後綴「手機克星」 |
| 百里 | 運動任務累計 1000 分鐘 | 後綴「百里行者」 |
| 藏書 | 閱讀任務累計 1000 分鐘 | 後綴「藏書者」 |
| 破繭 | 完成一個 66 天週期 | 前綴「破繭的」 |
| 收藏家 | 圖鑑擁有 10 種卡片 | 後綴「收藏家」 |
| 傳說 | 到達 S 階 | 前綴「傳說的」 |

- **任務角色**：由漸進任務推導，不新增欄位。`Goal.Category = Routine` 且 `ValueKind = TimeOfDay` → 就寢；`TimeOfDayEvening` → 起床；`Exercise`／`Reading`／`ScreenTime` 對應同名角色。一般任務沒有角色，不計入這幾個成就。
- **分類連續天數**：某角色的任務「到昨天為止連續幾天 `IsDone`，今天已完成則再 +1」。由 `TodayContextLoader` 一次批次查出近 31 天各漸進任務的完成日期，記憶體內計算；同角色在封存重建目標後以新任務重新起算。
- **分類累計分鐘**：運動、閱讀任務所有 `QuestProgress.Value` 加總，同一次批次查詢。
- **稱號組合**：`Player.TitlePrefixKey`、`TitleSuffixKey`，只能選已解鎖且槽位正確的字塊，可只選一邊或都不選。顯示規則：有選 → 「前綴・後綴」；都沒選 → 沿用階級稱號。

### 7. 主題

- `Player.ThemeKey`（`azure`／`violet`／`jade`，預設 `azure`）與已擁有主題清單 `OwnedTheme`（UserId、ThemeKey，唯一）；`azure` 不需購買。
- 前端依 `/me` 回傳的 `themeKey` 設定 `<html data-accent>`；設定頁提供切換與購買。

### 8. 偵測與持久化

- Domain 純規則 `Rewards.Evaluate(RewardInput input) → RewardOutcome`：
  - `RewardInput` 含請求前快照 `PlayerSnapshot(Level)`（只用來算回應的 `levelsGained`）、請求後 `Player`、已解鎖成就鍵集合、分類連續天數、分類累計分鐘、擁有卡片種數、本次完成的目標數、本次完成的週期數、是否建立了第一個目標、今日達標狀態的前後值。
  - `RewardOutcome` 含要建立的寶箱、要解鎖的成就、金幣事件、保險卡增減。
- `Loot.Open(rarity, ownedCounts, rng) → LootResult(cardId, isDuplicate, coins)`。
- `TodayContextLoader.LoadAsync` 在列鎖內、任何修改前拍 `PlayerSnapshot(Level)` 放入 `TodayContext.Before`；第 6 節所需的統計由 `RewardStatsLoader` 在第一次 `SaveChangesAsync` 之後批次載入（才看得到本次修改），目標完成由 `RewardApplier` 判定。
- 所有會改玩家狀態的服務（今日進度、任務增刪改、目標建立與封存、困難模式、開新週期、商店）在第一次 `SaveChangesAsync` 之後統一呼叫一次 `RewardApplier.ApplyAsync`（內部執行 `Rewards.Evaluate`）並寫入結果，與 XpEvent 的持久化一致。
- 保險卡消耗在 Domain `Settlement.Settle` 內處理（它本來就逐日判定達標），回傳的 `SettlementResult` 加上 `ShieldsUsed` 日期清單。

### 9. 資料模型

- `Player` 加 `Coins`、`ShieldCount`、`ThemeKey`、`TitlePrefixKey`、`TitleSuffixKey`、`PinnedCardId`。
- `Goal` 加 `CompletedAt`；`Program` 加 `CompletedAt`。
- 新表：`RewardChests`（Id、UserId、Rarity、Source、CreatedAt、OpenedAt、DroppedCardId、Coins）、`OwnedCards`（UserId、CardId、Count、FirstAcquiredAt；唯一 UserId＋CardId）、`Achievements`（UserId、Key、UnlockedAt；唯一）、`OwnedThemes`（UserId、ThemeKey；唯一）、`CoinEvents`（Id、Seq、UserId、Amount、Source、RefId、OccurredAt）、`RewardEvents`（Id、UserId、Kind、Date、OccurredAt，記保險卡生效等）。
- 一支 migration；enum 一律存字串。

### 10. API

- `GET /rewards`：金幣、保險卡、未開寶箱清單、成就清單（含是否解鎖與進度）、稱號字塊與目前組合、釘選卡。
- `POST /rewards/chests/{id}/open` → 掉落卡片、是否重複、金幣、更新後的金幣餘額。
- `GET /cards` → 目錄加擁有狀態與張數。
- `PUT /me/title` `{ prefixKey, suffixKey }`；`PUT /me/pinned-card` `{ cardId | null }`；`PUT /me/theme` `{ themeKey }`。
- `POST /shop/purchase` `{ item: "Shield" | "EChest" | "Theme", themeKey? }`。
- 既有會改狀態的回應（`/today`、`/today/quests/{id}/progress`、`/quests*`、`/goals*`、`/me`、`/program/restart`）加 `rewards` 欄位：本次升了幾級、新寶箱、新成就、金幣變動、保險卡生效紀錄。前端據此顯示系統訊息。
- `/me` 加 `coins`、`shieldCount`、`themeKey`、`title`（組合後字串）、`pinnedCard`。

### 11. 前端

- 導覽插入第三格「檔案 HUNTER」：狀態面板＋稱號組合器、金幣與保險卡、待開寶箱（開箱有翻牌動效與系統訊息）、成就清單（已解鎖亮起，未解鎖顯示條件與進度）、圖鑑網格、釘選卡。
- 設定頁加「主題」：三個主題卡，未擁有顯示價格與購買。
- 商店：放在檔案頁金幣旁的小視窗，兩項商品（保險卡、E 級寶箱）加一個前往設定頁買主題的連結。
- 系統訊息依 `rewards` 依序顯示：升級 → 晉階 → 寶箱 → 成就 → 保險卡生效。

### 12. 錯誤處理

- 開已開過的寶箱或別人的寶箱 → 404 `ChestNotFound`。
- 稱號字塊未解鎖或槽位錯 → 400 `TitleNotUnlocked`。
- 釘選未擁有的卡 → 400 `CardNotOwned`。
- 切換未擁有主題 → 400 `ThemeNotOwned`。

### 13. 測試

- Domain：`Rewards.Evaluate` 對升 1 級、一次跨 2 級、晉階、連續 7／30、無變化、同請求撤銷再完成、各成就條件的輸出；`Loot.Open` 的抽卡與重複轉金幣（固定亂數）；`Settlement` 保險卡逐日消耗、用完停止、困難模式懲罰照扣。
- Api：完成任務升級後回 E 箱與系統訊息資料；開箱加入收藏、重複轉金幣；商店扣款與各錯誤碼；保險卡在漏一天後自動生效、連續不斷；目標完成發 A 箱且只發一次；開新週期滿 66 天發 S 箱、未滿不發；分類連續 30 天解鎖對應字塊；稱號組合與主題切換的驗證。

## 驗收

- 新帳號從 Lv.1 升到 Lv.2 時看到「等級提升」與「獲得 E 級寶箱」兩則系統訊息，檔案頁可開箱並在圖鑑看到卡片。
- 漏一天且持有保險卡時，隔天連勝不中斷並看到保險生效訊息。
- 用金幣買到紫影後可切換，介面整體換色。
- 稱號可組合成「靜夜的・百戰獵人」並顯示在狀態面板。

## 這次不做

角色紙娃娃、自訂現實獎勵、地下城、碎片、卡片交換或合成、稱號特效、指定換卡、排名與分享。

## 之後可能加

地下城（用鑰匙開的短期挑戰，接寶箱）、66 天 Boss、更多主題、稱號特效、以金幣兌換指定卡片。
