# 引導式目標設定與漸進式任務 設計

## 目標

註冊後不再直接塞 9 個固定任務，而是先問使用者「想達成什麼、現況如何、幾天內」，由系統依固定類別的公式產生**每天目標逐步逼近終點**的任務。核心價值：難度從使用者的現況出發，避免一開始就是「60 下伏地挺身」這種讓人放棄的門檻。

## 背景與決定

- 沿用 SoloLeveling 現有機制：EXP、等級、五維屬性、66 天週期、達標率、結算都不變。漸進式任務只是「目標值會隨階段變」的任務。
- 第一版支援 4 個固定類別，每類有自己的問題與公式，不用 AI。
- 漸進節奏：使用者選目標天數，系統切成階梯，**達標的天數累積才升階，沒達標就停留在原階**。
- 時間點任務（就寢、起床）沿用 Check 型，每天只勾有沒有做到，不記實際時間。
- 現有帳號已有任務者不受影響，不會被要求引導。

## 假設

- 每階天數第一版固定 3 天，但存在任務欄位裡，之後開放調整不改模型。
- 原本的 9 個預設任務改為引導最後一步的「基本任務」勾選清單。
- 只有一個使用者（作者本人），不需要考慮舊資料搬移，但既有帳號的行為要維持。
- `docs/SPEC.md` §12 規定未列功能不得新增，本功能實作時要同步更新 SPEC。

## 設計

### 1. 目標類別（Domain：`GoalCategories`）

| 類別 | enum | 問題（回答鍵） | 產生任務 | 屬性／難度 | 取代的基本任務索引 |
| --- | --- | --- | --- | --- | --- |
| 作息 | `Routine` | `currentBedtime`、`targetBedtime`、`currentWakeTime`、`targetWakeTime`（HH:MM）、`lengthDays` | `{target} 前上床睡覺`（Check）、`{target} 前起床`（Check） | VIT／Normal、VIT／Hard | 0、1 |
| 運動 | `Exercise` | `currentMinutes`、`targetMinutes`、`lengthDays` | `運動`（Count，單位分鐘，Step 5） | STR／Normal | 7 |
| 閱讀 | `Reading` | `currentMinutes`、`targetMinutes`、`lengthDays` | `閱讀`（Count，單位分鐘，Step 5） | INT／Normal | 3 |
| 螢幕時間 | `ScreenTime` | `currentHours`、`targetHours`、`lengthDays` | `手機螢幕時間`（Limit，單位小時，Step 0.5） | WIL／Hard | 5 |

每個類別是一個 Domain 內的定義物件，包含：問題清單（鍵、標籤、型別、範圍）、驗證、以及 `Build(answers) → IReadOnlyList<QuestTemplate>` 產生任務樣板。新增類別只要加一個定義，不改其他層。

**驗證規則**（違反丟 `DomainValidationException`）：
- `lengthDays` 介於 7 到 90。
- 時間格式 `HH:MM`，分鐘為 0 到 59。
- 目標必須比現況「更好」：就寢與起床的目標不得晚於現況；運動與閱讀的目標分鐘不得小於現況；螢幕時間的目標小時不得大於現況。允許相等，相等時該任務仍建立，但起點等於終點、只有 1 階。
- 運動與閱讀 0 到 300 分鐘；螢幕時間 0 到 24 小時。

### 2. 時間編碼

時間點一律存成「距中午 12:00 的分鐘數」，範圍 0 到 1439：01:00 是 780、00:00 是 720、23:30 是 690、07:00 是 1140、08:00 是 1200。就寢時間跨午夜後仍單調，「提早」就是數值變小，比較與加減不需特判。顯示時轉回 HH:MM：`((minutes / 60) + 12) % 24`。

Domain 提供 `TimeOfDay.Parse("01:00") → 780` 與 `TimeOfDay.Format(780) → "01:00"`，是純函式。

### 3. 漸進參數與階段公式（Domain：`Progression`）

**Quest 新增欄位**（全部可為 null，全 null 代表一般任務，行為完全不變）：

| 欄位 | 型別 | 說明 |
| --- | --- | --- |
| `GoalId` | `Guid?` | 所屬目標 |
| `ValueKind` | `ProgressionValueKind?` | `Number` 或 `TimeOfDay` |
| `StartValue` | `decimal?` | 現況值 |
| `EndValue` | `decimal?` | 終點值 |
| `StepValue` | `decimal?` | 每階變化量，有正負 |
| `StageCount` | `int?` | 總階數 |
| `DaysPerStep` | `int?` | 每階需要的達標天數，第一版固定 3 |

**建立時的公式**：
- `StageCount = ceil(lengthDays / DaysPerStep)`，至少 1。
- `StepValue = (EndValue - StartValue) / StageCount`，依 `Granularity` 四捨五入：`TimeOfDay` 為 5 分鐘、分鐘類為 1、小時類為 0.25。四捨五入後為 0 但起終點不同時，取一個 granularity 的量並帶正確符號。

**每天的計算**：
- `doneDays` = 這個任務在**今天之前**所有 `QuestProgress.IsDone == true` 的天數。
- `Stage = min(StageCount, 1 + floor(doneDays / DaysPerStep))`。第 1 天就是第 1 階，目標已經比現況好一階。
- `EffectiveTarget = Stage < StageCount ? StartValue + Stage × StepValue : EndValue`。最後一階固定等於終點，四捨五入的誤差不會讓終點對不上。到達最後一階後任務持續存在，等於維持期。
- 今天自己的完成狀態不影響今天的目標，只影響明天。

**渲染**：
- Check 型：`Name` 存樣板，含 `{target}` 佔位，API 出口用 `TimeOfDay.Format(EffectiveTarget)` 替換成當天名字。
- Count／Limit 型：`EffectiveTarget` 直接當 `targetValue` 回前端，`Name` 不含佔位。
- 一律附 `stage`、`stageCount`、`targetLabel`（例如 `00:50` 或 `25 分鐘`）。

**判定**：`ProgressUpdater.SetValue` 與 `CompletionRules.IsDone` 改吃 `EffectiveTarget` 而非 `quest.TargetValue`。一般任務的 `EffectiveTarget` 就是 `TargetValue`。

**快照**：`QuestProgress` 新增 `TargetSnapshot: decimal?`，`SetValue` 時寫入當天判定用的目標，歷史才查得到「那天目標是多少」。

### 4. 資料模型

**新實體 `Goal`**

| 欄位 | 型別 | 說明 |
| --- | --- | --- |
| `Id` | `Guid` | |
| `UserId` | `Guid` | |
| `Category` | `GoalCategory` | 存字串 |
| `Answers` | `string` | 原始回答的 JSON（jsonb），保留給之後重新規劃或 AI 入口使用 |
| `LengthDays` | `int` | |
| `StartDate` | `DateOnly` | 建立當天（使用者時區） |
| `IsArchived`／`ArchivedAt` | `bool`／`DateTimeOffset?` | |
| `CreatedAt` | `DateTimeOffset` | |

索引：`(UserId, IsArchived)`；partial unique `(UserId, Category) WHERE IsArchived = false`，同類別同時只能有一個進行中的目標。

`Quest.GoalId` 為外鍵，`Goal` 封存時連帶封存其未封存任務。

**Migration**：`AddGoalsAndProgression`，新增 `Goals` 表、`Quests` 七個欄位、`QuestProgresses.TargetSnapshot`。

### 5. 達標天數的載入

`TodayContextLoader.LoadAsync` 多做一件事：對使用者所有 `GoalId != null` 且未封存的任務，**一次**查出「今天之前 `IsDone` 的天數」，做成 `IReadOnlyDictionary<Guid, int>` 放進 `TodayContext.DoneDaysBeforeToday`。查詢是 `QuestProgresses join DailyLogs` 過濾 `Date < today` 後 group by `QuestId`。不逐任務查。

### 6. API

所有端點在 `/api/v1`，需 Bearer。

**`GET /me` 新增欄位** `needsOnboarding: bool`：使用者沒有任何未封存任務時為 true。不加資料庫旗標。

**`GET /goals/categories`**：回類別定義給前端畫表單。

```json
{
  "categories": [
    {
      "category": "Routine",
      "title": "作息",
      "questions": [
        { "key": "currentBedtime", "label": "現在大概幾點睡", "type": "time" },
        { "key": "targetBedtime", "label": "希望幾點睡", "type": "time" },
        { "key": "currentWakeTime", "label": "現在大概幾點起床", "type": "time" },
        { "key": "targetWakeTime", "label": "希望幾點起床", "type": "time" },
        { "key": "lengthDays", "label": "幾天內達成", "type": "integer", "min": 7, "max": 90, "default": 30 }
      ],
      "replacesBasicQuestIndexes": [0, 1]
    }
  ],
  "basicQuests": [
    { "index": 0, "name": "23:30 前上床睡覺", "statType": "VIT", "difficulty": "Normal" }
  ]
}
```

`type` 只有 `time`、`integer`、`decimal` 三種。

**`POST /goals/preview`**：輸入同 `POST /goals`，只計算不寫入，回每個目標會產生的任務與階段摘要。

```json
{
  "goals": [
    {
      "category": "Routine",
      "quests": [
        { "name": "00:55 前上床睡覺", "questType": "Check", "statType": "VIT", "difficulty": "Normal",
          "startLabel": "01:00", "endLabel": "00:00", "stageCount": 10, "daysPerStep": 3, "stepLabel": "提早 5 分鐘" }
      ]
    }
  ]
}
```

**`POST /goals`**

```json
{
  "goals": [
    { "category": "Routine", "answers": { "currentBedtime": "01:00", "targetBedtime": "00:00", "currentWakeTime": "08:00", "targetWakeTime": "07:00", "lengthDays": 30 } },
    { "category": "Reading", "answers": { "currentMinutes": 10, "targetMinutes": 30, "lengthDays": 30 } }
  ],
  "basicQuestIndexes": [2, 4, 6, 8]
}
```

- 在一個交易內建立所有 Goal 與 Quest，`basicQuestIndexes` 從 `DefaultQuests.All` 複製成一般任務，SortOrder 接在目標任務之後。
- `basicQuestIndexes` 含被任一目標取代的索引 → 400 `BasicQuestReplaced`。
- 同類別已有進行中的目標 → 409 `GoalAlreadyActive`。
- 引導流程與設定頁「新增目標」共用此端點，後者只帶一個目標且 `basicQuestIndexes` 為空。
- 回 201 與 `GET /goals` 相同格式。

**`GET /goals`**：未封存的目標，各帶其任務的當前階段。

```json
{
  "goals": [
    { "id": "…", "category": "Routine", "title": "作息", "lengthDays": 30, "startDate": "2026-09-29",
      "quests": [ { "id": "…", "name": "00:55 前上床睡覺", "stage": 1, "stageCount": 10, "isArchived": false } ] }
  ]
}
```

**`DELETE /goals/{id}`**：封存目標與其未封存任務，重算今日達標率，回 204。

**`GET /today` 的任務項目新增** `progression`：一般任務為 null。

```json
{ "id": "…", "name": "00:55 前上床睡覺", "questType": "Check", "targetValue": null,
  "progression": { "goalId": "…", "stage": 1, "stageCount": 10, "targetLabel": "00:55" } }
```

**`PUT /quests/{id}` 對漸進任務的限制**：只允許改 `statType`、`difficulty`；`name`、`questType`、`targetValue`、`step`、`unit` 與現值不同 → 400 `ProgressionQuestLocked`。要改目標就封存目標重建。

**`DELETE /quests/{id}`**：漸進任務可單獨封存，目標不受影響。

**`POST /auth/register`**：不再建立預設任務，其餘不變。

### 7. 前端

- 新路由 `#onboarding`。登入或註冊後若 `needsOnboarding` 為 true 就導到這裡；引導完成前其他頁面都導回。
- 三步：
  1. 選類別，可多選，至少一個。
  2. 逐類別回答，表單依 `GET /goals/categories` 產生，`time` 用 `<input type="time">`。
  3. 預覽：呼叫 `POST /goals/preview` 顯示每個任務的起點、終點、階數、每階變化；下方列出基本任務勾選清單，被取代的預設不勾且不可勾。確認呼叫 `POST /goals`，成功後導到 `#today`。
- 今日頁：漸進任務顯示渲染後的名字，名字右側小字「第 1／10 階」。
- 設定頁：任務清單上方新增「目標」區塊，列出目標、類別、開始日、各任務階段，每個目標有「封存」（二次確認）。「新增目標」按鈕進入只做一個類別的引導流程，類別選單排除已有進行中的。編輯漸進任務時，鎖住的欄位停用。
- 進度頁：不動。

### 8. 錯誤處理

- 回答驗證在 Domain 類別定義內，錯誤訊息用繁體中文說明哪個欄位、為什麼。
- 模型繫結失敗沿用既有 `ValidationFailed` 格式。
- 未知 `category` → 400 `UnknownCategory`。

### 9. 測試

**Domain 單元測試**
- `TimeOfDay`：Parse／Format 往返，00:00、12:00、23:59 邊界。
- 每個類別：驗證規則各一個失敗案例；`Build` 對代表性輸入產生正確的起終點、階數、步進、四捨五入。
- `Progression`：`Stage` 在 doneDays 為 0、2、3、5、6、遠超過時的值；`EffectiveTarget` 每階與最後一階等於終點；起終點相同時只有 1 階。
- `CompletionRules.IsDone` 用 `EffectiveTarget` 判定 Count／Limit。

**Api 整合測試**
- 註冊後 `GET /me` 的 `needsOnboarding` 為 true，`GET /today` 沒有任務。
- `POST /goals/preview` 回正確摘要且不寫入。
- `POST /goals` 建立作息目標後，`GET /today` 出現兩個任務，名字含第 1 階時間；`needsOnboarding` 變 false。
- 連續 3 天勾完成並撥時間後，第 4 天目標提早一階；中間有一天沒勾，階段不變。
- `TargetSnapshot` 有寫入。
- 同類別重建回 409；`basicQuestIndexes` 含被取代索引回 400。
- `PUT /quests/{id}` 改漸進任務的目標回 400，改難度成功。
- `DELETE /goals/{id}` 後任務消失、達標率分母縮小。
- 既有帳號直接以 `db` 種一個任務後 `needsOnboarding` 為 false。

### 10. 文件

- `docs/SPEC.md`：§1 範圍加引導與漸進；新增 §4.11 目標與漸進（公式、編碼、階段規則）；§4.10 改為「基本任務」；§6 API 加 goals 端點與 `needsOnboarding`；§8 加引導畫面；§11 驗收加對應項目。
- `docs/ARCHITECTURE.md`：資料模型加 Goal 與 Quest 漸進欄位，請求生命週期補 `DoneDaysBeforeToday` 的載入。
- `README.md`：curl 範例加建立目標。
- `CLAUDE.md`：關鍵不變式加「判定與顯示一律用 `EffectiveTarget`，不直接讀 `TargetValue`」與「Goal 封存連帶封存任務」。

## 驗收

- 新註冊帳號登入後進入引導，選作息與閱讀、填現況與目標、預覽看到階數摘要、確認後今日頁出現任務且名字帶第 1 階時間。
- 連續勾完成 3 天後，第 4 天就寢任務的時間提早；某天不勾，隔天時間不變。
- 設定頁看得到目標與階段，封存目標後任務消失。
- 既有帳號登入行為不變。
- 全部測試綠燈，`dotnet format` 無差異。

## 這次不做

AI 解讀自由描述、依表現自動加速或退回、記錄實際時間與趨勢圖、每階天數可調的 UI、Program 66 天週期與目標連動、目標的編輯（只能封存重建）、推播提醒。

## 之後可能加

- 自由描述入口：引導第一步多一個「用文字描述目標」，由 AI 轉成「類別加回答」再走同一套公式，使用者仍看到預覽。`Goal.Answers` 已保留結構。
- 回顧建議：依歷史資料給「某任務卡在第 2 階，建議每階改 5 天」之類的建議。
- 每階天數在建立時可選。
