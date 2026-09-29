# 引導式目標設定與漸進式任務 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 註冊後由引導流程依固定類別的公式產生「每天目標逐階逼近終點」的任務，達標天數累積才升階。

**Architecture:** Domain 新增 `Goal` 實體、`Quest` 的漸進欄位、純規則 `TimeOfDay`／`Progression`／`GoalPlanner`；階段不存 DB，由「今天之前達標天數」算出。Api 新增 `GoalService` 與 `/goals` 端點，`TodayContextLoader` 一次批次載入達標天數，判定與顯示一律走 `Progression.EffectiveTarget`。前端加 `#onboarding` 三步流程與設定頁目標區塊。

**Tech Stack:** .NET 10、EF Core 10＋Npgsql（jsonb）、xUnit＋FluentAssertions＋Testcontainers、純 HTML＋JS 前端

**Spec:** `docs/superpowers/specs/2026-09-29-guided-goals-progression-design.md`

## Global Constraints

- 依賴方向 Api → Infrastructure → Domain；Domain 不得引用框架（`System.Text.Json` 屬 BCL，可用）。
- 建置開 `TreatWarningsAsErrors`、`EnforceCodeStyleInBuild`；src 專案 public 成員缺 XML 註解就建置失敗；`<param>`／`<returns>` 要完整。
- 控制流一律加大括號；`catch` 只接已知例外；時間來源注入 `TimeProvider`，禁止 `DateTime.Now`／`UtcNow`。
- 「今日」只能用 `UserClock.DateOf(now, user.TimeZoneId)` 或 `TodayContext.Today`。
- Player.Xp 的任何變動都要有對應 XpEvent；服務層改今日狀態一律「開交易 → `TodayContextLoader.LoadAsync` → 修改 → `SaveChangesAsync` → Commit」。
- 導覽集合新增的實體要明確 `db.QuestProgresses.AddRange`（見 `TodayService.SetProgressAsync`）。
- DB enum 存字串 `HasConversion<string>().HasMaxLength(16)`；金額／數值 `HasPrecision(10, 2)`；表與欄位 PascalCase。
- 錯誤格式 `{ error: { code, message } }`：Api 層丟 `ApiErrorException`，Domain 層丟 `DomainValidationException(code, message)`。
- 測試名稱用中文描述行為；Api 整合測試類別加 `[Collection(PostgresCollection.Name)]`，用 `ApiFactory`；`_factory.Clock.Advance(...)` 撥時間，起始 2026-09-28 10:00 UTC。
- 註解與文件繁體中文全形標點；commit `type: 主旨`，結尾 `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`；分批 commit，不 push。
- 直接在 `main` 上 commit（本專案指示）。
- 兩個計畫層級的決定：(1) 既有整合測試依賴註冊即有 9 個任務，改由 `ApiFactory.RegisterAsync` 在註冊後呼叫 `POST /goals` 帶入 `basicQuestIndexes = [0..8]`，測試不用逐一改；(2) 設定頁第一版不提供漸進任務的編輯 UI，只在 API 端鎖定欄位，目標區塊只列出任務與階段並提供封存目標。

## Review Focus

1. **就寢目標跨午夜**（現況 00:30、目標 23:30）：編碼後仍是「數值變小＝更早」，不得被判成「目標比現況晚」。→ Task 2 測試 `Plan_作息_跨午夜提早_合法且逐階變小`。
2. **四捨五入後累加會超過終點**（01:00 → 00:50、30 天：每階 −1 分鐘四捨五入成 −5，第 3 階就會衝過 00:50）：任何階段的有效目標不得超過終點。→ Task 1 測試 `EffectiveTarget_步進四捨五入後_不會越過終點`。
3. **回答缺鍵或格式錯**（`lengthDays` 傳 "abc"、缺 `targetBedtime`）：要回 400 且訊息指出欄位，不得 500。→ Task 2 測試 `Plan_缺少回答鍵_丟DomainValidationException` 與 Task 4 整合測試 `PostGoals_回答格式錯_回400`。
4. **封存目標後再建同類別**：partial unique index 只限進行中，封存後要能重建。→ Task 4 測試 `DeleteGoal_封存後任務消失且同類別可重建`。
5. **Count 漸進任務的判定要用當階目標而非 `TargetValue`**（`TargetValue` 為 null）：閱讀第 1 階目標 12 分鐘，填 12 要算完成、填 11 不算，且 `TargetSnapshot` 寫入 12。→ Task 5 測試 `SetProgress_Count漸進任務_以當階目標判定並寫入快照`。

---

### Task 1: Domain 基礎：enum、時間編碼、實體欄位、階段公式

**Files:**
- Modify: `src/SoloLeveling.Domain/Enums.cs`（檔尾新增兩個 enum）
- Modify: `src/SoloLeveling.Domain/Entities/Quest.cs:45`（`CreatedAt` 之後新增欄位）
- Modify: `src/SoloLeveling.Domain/Entities/QuestProgress.cs:73`（`StatGranted` 之後新增欄位）
- Create: `src/SoloLeveling.Domain/Entities/Goal.cs`
- Create: `src/SoloLeveling.Domain/Rules/TimeOfDay.cs`
- Create: `src/SoloLeveling.Domain/Rules/Progression.cs`
- Test: `tests/SoloLeveling.Domain.Tests/TimeOfDayTests.cs`、`tests/SoloLeveling.Domain.Tests/ProgressionTests.cs`

**Interfaces:**
- Consumes: 既有 `Quest`、`QuestProgress`、`DomainValidationException(string code, string message)`。
- Produces:
  - `enum GoalCategory { Routine = 1, Exercise = 2, Reading = 3, ScreenTime = 4 }`
  - `enum ProgressionValueKind { Number = 1, TimeOfDay = 2 }`
  - `Quest` 新增 `Guid? GoalId`、`ProgressionValueKind? ValueKind`、`decimal? StartValue`、`decimal? EndValue`、`decimal? StepValue`、`int? StageCount`、`int? DaysPerStep`
  - `QuestProgress.TargetSnapshot: decimal?`
  - `Goal` 實體
  - `TimeOfDay.Parse(string hhmm) → int`、`TimeOfDay.Format(decimal minutesFromNoon) → string`
  - `Progression.DefaultDaysPerStep = 3`、`Progression.IsProgression(Quest)`、`Progression.StageCountFor(int lengthDays, int daysPerStep)`、`Progression.StepFor(decimal start, decimal end, int stageCount, decimal granularity)`、`Progression.StageOf(Quest, int doneDaysBeforeToday)`、`Progression.EffectiveTarget(Quest, int doneDaysBeforeToday) → decimal?`、`Progression.RenderName(Quest, int doneDaysBeforeToday) → string`、`Progression.TargetLabel(Quest, decimal target) → string`

- [ ] **Step 1: 寫失敗的測試**

建立 `tests/SoloLeveling.Domain.Tests/TimeOfDayTests.cs`：

```csharp
using FluentAssertions;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class TimeOfDayTests
{
    [Theory]
    [InlineData("12:00", 0)]
    [InlineData("23:30", 690)]
    [InlineData("00:00", 720)]
    [InlineData("01:00", 780)]
    [InlineData("07:00", 1140)]
    [InlineData("08:00", 1200)]
    [InlineData("11:59", 1439)]
    public void Parse_HHMM_轉成距中午的分鐘數(string text, int expected)
    {
        TimeOfDay.Parse(text).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "12:00")]
    [InlineData(690, "23:30")]
    [InlineData(720, "00:00")]
    [InlineData(780, "01:00")]
    [InlineData(1200, "08:00")]
    [InlineData(1439, "11:59")]
    public void Format_分鐘數_轉回HHMM(int minutes, string expected)
    {
        TimeOfDay.Format(minutes).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1:00")]
    [InlineData("25:00")]
    [InlineData("12:60")]
    [InlineData("abc")]
    public void Parse_格式錯誤_丟DomainValidationException(string text)
    {
        var act = () => TimeOfDay.Parse(text);
        act.Should().Throw<DomainValidationException>().Which.Code.Should().Be("InvalidTime");
    }
}
```

建立 `tests/SoloLeveling.Domain.Tests/ProgressionTests.cs`：

```csharp
using FluentAssertions;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class ProgressionTests
{
    private static Quest Bedtime(decimal start, decimal end, int stageCount, decimal step)
    {
        return new Quest
        {
            Id = Guid.NewGuid(),
            Name = "{target} 前上床睡覺",
            QuestType = QuestType.Check,
            GoalId = Guid.NewGuid(),
            ValueKind = ProgressionValueKind.TimeOfDay,
            StartValue = start,
            EndValue = end,
            StepValue = step,
            StageCount = stageCount,
            DaysPerStep = 3,
        };
    }

    private static Quest Reading(decimal start, decimal end, int stageCount, decimal step)
    {
        return new Quest
        {
            Id = Guid.NewGuid(),
            Name = "閱讀",
            QuestType = QuestType.Count,
            Unit = "分鐘",
            GoalId = Guid.NewGuid(),
            ValueKind = ProgressionValueKind.Number,
            StartValue = start,
            EndValue = end,
            StepValue = step,
            StageCount = stageCount,
            DaysPerStep = 3,
        };
    }

    [Theory]
    [InlineData(30, 3, 10)]
    [InlineData(7, 3, 3)]
    [InlineData(9, 3, 3)]
    [InlineData(1, 3, 1)]
    public void StageCountFor_天數除以每階天數無條件進位(int lengthDays, int daysPerStep, int expected)
    {
        Progression.StageCountFor(lengthDays, daysPerStep).Should().Be(expected);
    }

    [Theory]
    [InlineData(780, 720, 10, 5, -5)]   // 01:00→00:00，每階 -6 四捨五入到 5 分鐘 → -5
    [InlineData(780, 770, 10, 5, -5)]   // 差 10 分鐘切 10 階 → -1 四捨五入成 0，取一個 granularity
    [InlineData(10, 30, 10, 1, 2)]
    [InlineData(3, 2, 10, 0.25, -0.25)] // 小時類：-0.1 → -0.25
    [InlineData(720, 720, 1, 5, 0)]
    public void StepFor_依granularity四捨五入且不為零(decimal start, decimal end, int stageCount, decimal granularity, decimal expected)
    {
        Progression.StepFor(start, end, stageCount, granularity).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(5, 2)]
    [InlineData(6, 3)]
    [InlineData(27, 10)]
    [InlineData(100, 10)]
    public void StageOf_達標天數每3天升一階且不超過總階數(int doneDays, int expected)
    {
        Progression.StageOf(Bedtime(780, 720, 10, -5), doneDays).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 775)]   // 第 1 階 00:55
    [InlineData(3, 770)]   // 第 2 階 00:50
    [InlineData(26, 735)]  // 第 9 階 00:15
    [InlineData(27, 720)]  // 第 10 階＝終點 00:00
    [InlineData(99, 720)]
    public void EffectiveTarget_每階前進一步_最後一階等於終點(int doneDays, decimal expected)
    {
        Progression.EffectiveTarget(Bedtime(780, 720, 10, -5), doneDays).Should().Be(expected);
    }

    [Fact]
    public void EffectiveTarget_步進四捨五入後_不會越過終點()
    {
        // 01:00 → 00:50、10 階、每階 -5：第 2 階已到 770，第 3 階起必須停在 770
        var quest = Bedtime(780, 770, 10, -5);
        Progression.EffectiveTarget(quest, 6).Should().Be(770);
        Progression.EffectiveTarget(quest, 15).Should().Be(770);
    }

    [Fact]
    public void EffectiveTarget_起點等於終點_只有一階且等於終點()
    {
        Progression.EffectiveTarget(Bedtime(720, 720, 1, 0), 0).Should().Be(720);
        Progression.StageOf(Bedtime(720, 720, 1, 0), 30).Should().Be(1);
    }

    [Fact]
    public void EffectiveTarget_一般任務_回TargetValue()
    {
        var quest = new Quest { QuestType = QuestType.Count, TargetValue = 8 };
        Progression.IsProgression(quest).Should().BeFalse();
        Progression.EffectiveTarget(quest, 5).Should().Be(8);
    }

    [Fact]
    public void RenderName_時間類_以當階時間取代佔位()
    {
        Progression.RenderName(Bedtime(780, 720, 10, -5), 3).Should().Be("00:50 前上床睡覺");
    }

    [Fact]
    public void RenderName_一般任務_原名()
    {
        Progression.RenderName(new Quest { Name = "喝水" }, 0).Should().Be("喝水");
    }

    [Fact]
    public void TargetLabel_時間類與數字類()
    {
        Progression.TargetLabel(Bedtime(780, 720, 10, -5), 775).Should().Be("00:55");
        Progression.TargetLabel(Reading(10, 30, 10, 2), 12).Should().Be("12 分鐘");
        Progression.TargetLabel(Reading(3, 2, 4, -0.25m), 2.75m).Should().Be("2.75 分鐘");
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/SoloLeveling.Domain.Tests --filter "FullyQualifiedName~TimeOfDayTests|FullyQualifiedName~ProgressionTests"`
Expected: 建置失敗，錯誤提到 `TimeOfDay`、`Progression`、`GoalId`、`ProgressionValueKind` 不存在。

- [ ] **Step 3: 加 enum**

在 `src/SoloLeveling.Domain/Enums.cs` 檔尾加：

```csharp

/// <summary>
/// 引導式目標的類別；每個類別有自己的問題與漸進公式（見 <c>GoalCategories</c>）。
/// </summary>
public enum GoalCategory
{
    /// <summary>作息：就寢與起床時間。</summary>
    Routine = 1,

    /// <summary>運動：每天分鐘數。</summary>
    Exercise = 2,

    /// <summary>閱讀：每天分鐘數。</summary>
    Reading = 3,

    /// <summary>螢幕時間：每天小時上限。</summary>
    ScreenTime = 4,
}

/// <summary>
/// 漸進任務的目標值種類，決定顯示與四捨五入方式。
/// </summary>
public enum ProgressionValueKind
{
    /// <summary>一般數字（分鐘、小時等）。</summary>
    Number = 1,

    /// <summary>一天中的時間點，存成距中午 12:00 的分鐘數（見 <c>TimeOfDay</c>）。</summary>
    TimeOfDay = 2,
}
```

- [ ] **Step 4: 加實體欄位與 Goal 實體**

`src/SoloLeveling.Domain/Entities/Quest.cs` 在 `CreatedAt` 之後加：

```csharp

    /// <summary>所屬的引導式目標；null 表示一般任務，以下漸進欄位全為 null。</summary>
    public Guid? GoalId { get; set; }

    /// <summary>漸進目標值的種類。</summary>
    public ProgressionValueKind? ValueKind { get; set; }

    /// <summary>漸進起點（使用者的現況）。時間類為距中午的分鐘數。</summary>
    public decimal? StartValue { get; set; }

    /// <summary>漸進終點（使用者的目標）。</summary>
    public decimal? EndValue { get; set; }

    /// <summary>每升一階的變化量，帶正負號。</summary>
    public decimal? StepValue { get; set; }

    /// <summary>總階數；達到最後一階後目標固定為 <see cref="EndValue"/>。</summary>
    public int? StageCount { get; set; }

    /// <summary>升一階需要累積的達標天數（不需連續）。</summary>
    public int? DaysPerStep { get; set; }
```

`src/SoloLeveling.Domain/Entities/QuestProgress.cs` 在 `StatGranted` 之後加：

```csharp

    /// <summary>寫入進度當下用來判定的目標值（漸進任務為當階目標，一般任務為 TargetValue）；歷史查詢用。</summary>
    public decimal? TargetSnapshot { get; set; }
```

建立 `src/SoloLeveling.Domain/Entities/Goal.cs`：

```csharp
namespace SoloLeveling.Domain.Entities;

/// <summary>
/// 引導式目標：使用者對某類別的回答與期限，產生一到多個漸進任務。刪除只做封存。
/// </summary>
public class Goal
{
    /// <summary>主鍵。</summary>
    public Guid Id { get; set; }

    /// <summary>所屬使用者。</summary>
    public Guid UserId { get; set; }

    /// <summary>類別。</summary>
    public GoalCategory Category { get; set; }

    /// <summary>原始回答的 JSON 物件字串（鍵為問題 key，值為字串）；保留給日後重新規劃使用。</summary>
    public string Answers { get; set; } = "{}";

    /// <summary>目標天數（7–90）。</summary>
    public int LengthDays { get; set; }

    /// <summary>建立當天（使用者時區）。</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>是否已封存；封存時連帶封存其任務。</summary>
    public bool IsArchived { get; set; }

    /// <summary>封存時間（UTC）。</summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>建立時間（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
```

- [ ] **Step 5: 寫 TimeOfDay**

建立 `src/SoloLeveling.Domain/Rules/TimeOfDay.cs`：

```csharp
using System.Globalization;

namespace SoloLeveling.Domain.Rules;

/// <summary>
/// 時間點的編碼：存成「距中午 12:00 的分鐘數」（0–1439）。就寢時間跨午夜後仍單調（01:00 為 780、00:00 為 720），「提早」就是數值變小。
/// </summary>
public static class TimeOfDay
{
    /// <summary>一天的分鐘數。</summary>
    public const int MinutesPerDay = 1440;

    /// <summary>
    /// 把 <c>HH:MM</c> 轉成距中午的分鐘數。
    /// </summary>
    /// <param name="text">兩位小時、冒號、兩位分鐘，例如 <c>01:00</c>。</param>
    /// <returns>0–1439。</returns>
    /// <exception cref="DomainValidationException">格式不是 HH:MM 或超出範圍。</exception>
    public static int Parse(string text)
    {
        if (text.Length != 5 || text[2] != ':'
            || !int.TryParse(text.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hour)
            || !int.TryParse(text.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minute)
            || hour > 23 || minute > 59)
        {
            throw new DomainValidationException("InvalidTime", $"時間格式須為 HH:MM，收到「{text}」");
        }

        return ((hour + 12) % 24) * 60 + minute;
    }

    /// <summary>
    /// 把距中午的分鐘數轉回 <c>HH:MM</c>。
    /// </summary>
    /// <param name="minutesFromNoon">0–1439；有小數會四捨五入。</param>
    /// <returns><c>HH:MM</c>。</returns>
    public static string Format(decimal minutesFromNoon)
    {
        var total = ((int)Math.Round(minutesFromNoon, MidpointRounding.AwayFromZero) % MinutesPerDay + MinutesPerDay) % MinutesPerDay;
        var hour = (total / 60 + 12) % 24;
        var minute = total % 60;
        return $"{hour:00}:{minute:00}";
    }
}
```

- [ ] **Step 6: 寫 Progression**

建立 `src/SoloLeveling.Domain/Rules/Progression.cs`：

```csharp
using SoloLeveling.Domain.Entities;

namespace SoloLeveling.Domain.Rules;

/// <summary>
/// 漸進任務的階段規則。階段不存 DB，由「今天之前的達標天數」算出：達標天數累積才升階，沒達標就停留。
/// 一般任務（<see cref="Quest.GoalId"/> 為 null）的有效目標就是 <see cref="Quest.TargetValue"/>。
/// </summary>
public static class Progression
{
    /// <summary>每升一階需要的達標天數（第一版固定）。</summary>
    public const int DefaultDaysPerStep = 3;

    /// <summary>名稱樣板中被當階目標取代的佔位字串。</summary>
    public const string TargetPlaceholder = "{target}";

    /// <summary>
    /// 是否為漸進任務。
    /// </summary>
    /// <param name="quest">任務。</param>
    /// <returns>有 GoalId 即為漸進任務。</returns>
    public static bool IsProgression(Quest quest)
    {
        return quest.GoalId != null;
    }

    /// <summary>
    /// 目標天數切成幾階：<c>ceil(lengthDays / daysPerStep)</c>，至少 1。
    /// </summary>
    /// <param name="lengthDays">目標天數。</param>
    /// <param name="daysPerStep">每階天數。</param>
    /// <returns>總階數。</returns>
    public static int StageCountFor(int lengthDays, int daysPerStep)
    {
        return Math.Max(1, (lengthDays + daysPerStep - 1) / daysPerStep);
    }

    /// <summary>
    /// 每階變化量：總差距平均分到各階後依 granularity 四捨五入；四捨五入成 0 但起終點不同時，取一個 granularity 並帶正確符號。
    /// </summary>
    /// <param name="start">起點。</param>
    /// <param name="end">終點。</param>
    /// <param name="stageCount">總階數。</param>
    /// <param name="granularity">四捨五入單位（時間類 5 分鐘、分鐘類 1、小時類 0.25）。</param>
    /// <returns>帶符號的每階變化量；起終點相同為 0。</returns>
    public static decimal StepFor(decimal start, decimal end, int stageCount, decimal granularity)
    {
        var diff = end - start;
        if (diff == 0)
        {
            return 0;
        }

        var raw = diff / stageCount;
        var rounded = Math.Round(raw / granularity, MidpointRounding.AwayFromZero) * granularity;
        return rounded == 0 ? Math.Sign(diff) * granularity : rounded;
    }

    /// <summary>
    /// 今天在第幾階（從 1 起）：<c>min(StageCount, 1 + doneDays / DaysPerStep)</c>。
    /// </summary>
    /// <param name="quest">漸進任務。</param>
    /// <param name="doneDaysBeforeToday">今天之前的達標天數。</param>
    /// <returns>1 到 StageCount。</returns>
    public static int StageOf(Quest quest, int doneDaysBeforeToday)
    {
        var stageCount = quest.StageCount ?? 1;
        var daysPerStep = quest.DaysPerStep ?? DefaultDaysPerStep;
        return Math.Min(stageCount, 1 + doneDaysBeforeToday / daysPerStep);
    }

    /// <summary>
    /// 今天的有效目標。漸進任務：最後一階固定等於終點，其餘為 <c>Start + Stage × Step</c> 且不越過終點；一般任務：<see cref="Quest.TargetValue"/>。
    /// </summary>
    /// <param name="quest">任務。</param>
    /// <param name="doneDaysBeforeToday">今天之前的達標天數（一般任務忽略）。</param>
    /// <returns>有效目標；一般 Check 任務為 null。</returns>
    public static decimal? EffectiveTarget(Quest quest, int doneDaysBeforeToday)
    {
        if (!IsProgression(quest))
        {
            return quest.TargetValue;
        }

        var start = quest.StartValue ?? 0;
        var end = quest.EndValue ?? start;
        var step = quest.StepValue ?? 0;
        var stage = StageOf(quest, doneDaysBeforeToday);
        if (stage >= (quest.StageCount ?? 1))
        {
            return end;
        }

        var raw = start + stage * step;
        return step < 0 ? Math.Max(end, raw) : Math.Min(end, raw);
    }

    /// <summary>
    /// 顯示名稱：時間類把名稱樣板的 <c>{target}</c> 換成當階時間；其他原樣。
    /// </summary>
    /// <param name="quest">任務。</param>
    /// <param name="doneDaysBeforeToday">今天之前的達標天數。</param>
    /// <returns>顯示名稱。</returns>
    public static string RenderName(Quest quest, int doneDaysBeforeToday)
    {
        if (quest.ValueKind != ProgressionValueKind.TimeOfDay)
        {
            return quest.Name;
        }

        var target = EffectiveTarget(quest, doneDaysBeforeToday) ?? 0;
        return quest.Name.Replace(TargetPlaceholder, TimeOfDay.Format(target), StringComparison.Ordinal);
    }

    /// <summary>
    /// 目標的顯示文字：時間類 <c>HH:MM</c>，數字類 <c>數值 單位</c>。
    /// </summary>
    /// <param name="quest">漸進任務。</param>
    /// <param name="target">目標值。</param>
    /// <returns>顯示文字。</returns>
    public static string TargetLabel(Quest quest, decimal target)
    {
        if (quest.ValueKind == ProgressionValueKind.TimeOfDay)
        {
            return TimeOfDay.Format(target);
        }

        var number = target.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(quest.Unit) ? number : $"{number} {quest.Unit}";
    }
}
```

- [ ] **Step 7: 跑測試確認通過**

Run: `dotnet test tests/SoloLeveling.Domain.Tests --filter "FullyQualifiedName~TimeOfDayTests|FullyQualifiedName~ProgressionTests"`
Expected: 全部 PASS（TimeOfDay 18 個、Progression 21 個）。

再跑：`dotnet build && dotnet format --verify-no-changes`
Expected: 0 錯誤、無格式差異。

- [ ] **Step 8: Commit**

```bash
git add src/SoloLeveling.Domain tests/SoloLeveling.Domain.Tests/TimeOfDayTests.cs tests/SoloLeveling.Domain.Tests/ProgressionTests.cs
git commit -m "feat: 新增目標類別 enum、時間編碼與漸進階段規則" -m "1. 漸進任務需要「起點、終點、每階變化量、階數」與階段公式，加在 Domain 讓 Api 各處統一取當階目標
2. 時間點以距中午的分鐘數編碼，跨午夜仍單調，提早即數值變小
3. Goal 實體與 QuestProgress.TargetSnapshot 一併加入，供後續 migration" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: Domain 目標類別定義與規劃器

**Files:**
- Create: `src/SoloLeveling.Domain/Goals/GoalQuestion.cs`
- Create: `src/SoloLeveling.Domain/Goals/GoalQuestBlueprint.cs`
- Create: `src/SoloLeveling.Domain/Goals/GoalCategoryDefinition.cs`
- Create: `src/SoloLeveling.Domain/Goals/RoutineGoal.cs`、`ExerciseGoal.cs`、`ReadingGoal.cs`、`ScreenTimeGoal.cs`
- Create: `src/SoloLeveling.Domain/Goals/GoalCategories.cs`
- Create: `src/SoloLeveling.Domain/Goals/GoalPlanner.cs`
- Test: `tests/SoloLeveling.Domain.Tests/GoalPlannerTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `TimeOfDay.Parse`、`Progression.StageCountFor`、`Progression.StepFor`、`Progression.DefaultDaysPerStep`、`GoalCategory`、`ProgressionValueKind`。
- Produces:
  - `enum GoalQuestionType { Time = 1, Integer = 2, Decimal = 3 }`
  - `record GoalQuestion(string Key, string Label, GoalQuestionType Type, decimal? Min, decimal? Max, decimal? Default)`
  - `record GoalQuestBlueprint(string Name, StatType StatType, Difficulty Difficulty, QuestType QuestType, ProgressionValueKind ValueKind, decimal StartValue, decimal EndValue, decimal StepValue, int StageCount, int DaysPerStep, decimal? UiStep, string? Unit)`
  - `abstract class GoalCategoryDefinition { GoalCategory Category; string Title; IReadOnlyList<GoalQuestion> Questions; IReadOnlyList<int> ReplacesBasicQuestIndexes; abstract IReadOnlyList<GoalQuestBlueprint> Build(IReadOnlyDictionary<string, string> answers, int lengthDays); }`
  - `GoalCategories.All: IReadOnlyList<GoalCategoryDefinition>`、`GoalCategories.Get(GoalCategory)`
  - `GoalPlanner.Plan(GoalCategory category, IReadOnlyDictionary<string, string> answers) → IReadOnlyList<GoalQuestBlueprint>`（驗證 lengthDays 並呼叫定義的 Build）
  - `GoalPlanner.LengthDaysOf(answers) → int`

- [ ] **Step 1: 寫失敗的測試**

建立 `tests/SoloLeveling.Domain.Tests/GoalPlannerTests.cs`：

```csharp
using FluentAssertions;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Goals;

namespace SoloLeveling.Domain.Tests;

public class GoalPlannerTests
{
    private static Dictionary<string, string> Routine(string bed, string targetBed, string wake, string targetWake, string days = "30")
    {
        return new Dictionary<string, string>
        {
            ["currentBedtime"] = bed,
            ["targetBedtime"] = targetBed,
            ["currentWakeTime"] = wake,
            ["targetWakeTime"] = targetWake,
            ["lengthDays"] = days,
        };
    }

    [Fact]
    public void Plan_作息_產生就寢與起床兩個Check任務()
    {
        var quests = GoalPlanner.Plan(GoalCategory.Routine, Routine("01:00", "00:00", "08:00", "07:00"));

        quests.Should().HaveCount(2);
        var bed = quests[0];
        bed.Name.Should().Be("{target} 前上床睡覺");
        bed.QuestType.Should().Be(QuestType.Check);
        bed.StatType.Should().Be(StatType.Vitality);
        bed.Difficulty.Should().Be(Difficulty.Normal);
        bed.ValueKind.Should().Be(ProgressionValueKind.TimeOfDay);
        bed.StartValue.Should().Be(780);
        bed.EndValue.Should().Be(720);
        bed.StageCount.Should().Be(10);
        bed.StepValue.Should().Be(-5);
        bed.DaysPerStep.Should().Be(3);
        var wake = quests[1];
        wake.Name.Should().Be("{target} 前起床");
        wake.Difficulty.Should().Be(Difficulty.Hard);
        wake.StartValue.Should().Be(1200);
        wake.EndValue.Should().Be(1140);
    }

    [Fact]
    public void Plan_作息_跨午夜提早_合法且逐階變小()
    {
        var quests = GoalPlanner.Plan(GoalCategory.Routine, Routine("00:30", "23:30", "08:00", "08:00"));

        quests[0].StartValue.Should().Be(750);
        quests[0].EndValue.Should().Be(690);
        quests[0].StepValue.Should().BeNegative();
        quests[1].StartValue.Should().Be(quests[1].EndValue);
        quests[1].StageCount.Should().Be(10);
        quests[1].StepValue.Should().Be(0);
    }

    [Fact]
    public void Plan_作息_目標比現況晚_丟例外()
    {
        var act = () => GoalPlanner.Plan(GoalCategory.Routine, Routine("00:00", "01:00", "08:00", "07:00"));
        act.Should().Throw<DomainValidationException>().Which.Code.Should().Be("TargetNotBetter");
    }

    [Fact]
    public void Plan_閱讀_產生Count任務且目標遞增()
    {
        var answers = new Dictionary<string, string> { ["currentMinutes"] = "10", ["targetMinutes"] = "30", ["lengthDays"] = "30" };

        var quests = GoalPlanner.Plan(GoalCategory.Reading, answers);

        quests.Should().ContainSingle();
        var q = quests[0];
        q.Name.Should().Be("閱讀");
        q.QuestType.Should().Be(QuestType.Count);
        q.StatType.Should().Be(StatType.Intelligence);
        q.ValueKind.Should().Be(ProgressionValueKind.Number);
        q.Unit.Should().Be("分鐘");
        q.UiStep.Should().Be(5);
        q.StartValue.Should().Be(10);
        q.EndValue.Should().Be(30);
        q.StageCount.Should().Be(10);
        q.StepValue.Should().Be(2);
    }

    [Fact]
    public void Plan_運動_目標不得小於現況()
    {
        var answers = new Dictionary<string, string> { ["currentMinutes"] = "30", ["targetMinutes"] = "20", ["lengthDays"] = "30" };
        var act = () => GoalPlanner.Plan(GoalCategory.Exercise, answers);
        act.Should().Throw<DomainValidationException>().Which.Code.Should().Be("TargetNotBetter");
    }

    [Fact]
    public void Plan_螢幕時間_產生Limit任務且上限遞減()
    {
        var answers = new Dictionary<string, string> { ["currentHours"] = "4", ["targetHours"] = "2", ["lengthDays"] = "21" };

        var q = GoalPlanner.Plan(GoalCategory.ScreenTime, answers).Single();

        q.QuestType.Should().Be(QuestType.Limit);
        q.StatType.Should().Be(StatType.Willpower);
        q.Difficulty.Should().Be(Difficulty.Hard);
        q.Unit.Should().Be("小時");
        q.UiStep.Should().Be(0.5m);
        q.StageCount.Should().Be(7);
        q.StepValue.Should().Be(-0.25m);
    }

    [Theory]
    [InlineData("6")]
    [InlineData("91")]
    [InlineData("abc")]
    public void Plan_天數超出範圍或非數字_丟例外(string days)
    {
        var act = () => GoalPlanner.Plan(GoalCategory.Routine, Routine("01:00", "00:00", "08:00", "07:00", days));
        act.Should().Throw<DomainValidationException>().Which.Code.Should().Be("InvalidLengthDays");
    }

    [Fact]
    public void Plan_缺少回答鍵_丟DomainValidationException()
    {
        var answers = new Dictionary<string, string> { ["currentMinutes"] = "10", ["lengthDays"] = "30" };
        var act = () => GoalPlanner.Plan(GoalCategory.Reading, answers);
        act.Should().Throw<DomainValidationException>().Which.Code.Should().Be("MissingAnswer");
    }

    [Fact]
    public void Plan_數值超出問題範圍_丟例外()
    {
        var answers = new Dictionary<string, string> { ["currentMinutes"] = "10", ["targetMinutes"] = "999", ["lengthDays"] = "30" };
        var act = () => GoalPlanner.Plan(GoalCategory.Reading, answers);
        act.Should().Throw<DomainValidationException>().Which.Code.Should().Be("InvalidAnswer");
    }

    [Fact]
    public void GoalCategories_四個類別且取代索引正確()
    {
        GoalCategories.All.Select(c => c.Category).Should().Equal(GoalCategory.Routine, GoalCategory.Exercise, GoalCategory.Reading, GoalCategory.ScreenTime);
        GoalCategories.Get(GoalCategory.Routine).ReplacesBasicQuestIndexes.Should().Equal(0, 1);
        GoalCategories.Get(GoalCategory.Exercise).ReplacesBasicQuestIndexes.Should().Equal(7);
        GoalCategories.Get(GoalCategory.Reading).ReplacesBasicQuestIndexes.Should().Equal(3);
        GoalCategories.Get(GoalCategory.ScreenTime).ReplacesBasicQuestIndexes.Should().Equal(5);
        GoalCategories.All.Should().OnlyContain(c => c.Questions.Any(q => q.Key == "lengthDays"));
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/SoloLeveling.Domain.Tests --filter "FullyQualifiedName~GoalPlannerTests"`
Expected: 建置失敗，`GoalPlanner`、`GoalCategories` 不存在。

- [ ] **Step 3: 寫基礎型別**

建立 `src/SoloLeveling.Domain/Goals/GoalQuestion.cs`：

```csharp
namespace SoloLeveling.Domain.Goals;

/// <summary>
/// 引導問題的回答型別。
/// </summary>
public enum GoalQuestionType
{
    /// <summary>時間 HH:MM。</summary>
    Time = 1,

    /// <summary>整數。</summary>
    Integer = 2,

    /// <summary>小數。</summary>
    Decimal = 3,
}

/// <summary>
/// 引導流程中的一個問題；前端依此產生表單，後端依此驗證回答。
/// </summary>
/// <param name="Key">回答的鍵。</param>
/// <param name="Label">顯示文字。</param>
/// <param name="Type">回答型別。</param>
/// <param name="Min">數字型的最小值；時間型為 null。</param>
/// <param name="Max">數字型的最大值；時間型為 null。</param>
/// <param name="Default">預設值；無則 null。</param>
public record GoalQuestion(string Key, string Label, GoalQuestionType Type, decimal? Min, decimal? Max, decimal? Default);
```

建立 `src/SoloLeveling.Domain/Goals/GoalQuestBlueprint.cs`：

```csharp
namespace SoloLeveling.Domain.Goals;

/// <summary>
/// 規劃器算出的漸進任務藍圖；Api 依此建立 <see cref="Entities.Quest"/>。
/// </summary>
/// <param name="Name">名稱；時間類含 <c>{target}</c> 佔位。</param>
/// <param name="StatType">屬性。</param>
/// <param name="Difficulty">難度。</param>
/// <param name="QuestType">任務類型。</param>
/// <param name="ValueKind">目標值種類。</param>
/// <param name="StartValue">起點。</param>
/// <param name="EndValue">終點。</param>
/// <param name="StepValue">每階變化量。</param>
/// <param name="StageCount">總階數。</param>
/// <param name="DaysPerStep">每階達標天數。</param>
/// <param name="UiStep">前端 −／＋ 的增減量；Check 為 null。</param>
/// <param name="Unit">單位；Check 為 null。</param>
public record GoalQuestBlueprint(
    string Name,
    StatType StatType,
    Difficulty Difficulty,
    QuestType QuestType,
    ProgressionValueKind ValueKind,
    decimal StartValue,
    decimal EndValue,
    decimal StepValue,
    int StageCount,
    int DaysPerStep,
    decimal? UiStep,
    string? Unit);
```

建立 `src/SoloLeveling.Domain/Goals/GoalCategoryDefinition.cs`：

```csharp
using System.Globalization;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Goals;

/// <summary>
/// 一個目標類別的定義：問題、驗證與產生漸進任務的公式。新增類別只要新增子類別並登記到 <see cref="GoalCategories"/>。
/// </summary>
public abstract class GoalCategoryDefinition
{
    /// <summary>類別。</summary>
    public abstract GoalCategory Category { get; }

    /// <summary>顯示名稱。</summary>
    public abstract string Title { get; }

    /// <summary>問題清單，最後一題一律是 <c>lengthDays</c>。</summary>
    public abstract IReadOnlyList<GoalQuestion> Questions { get; }

    /// <summary>此類別會取代的基本任務索引（對應 <see cref="DefaultQuests.All"/>）。</summary>
    public abstract IReadOnlyList<int> ReplacesBasicQuestIndexes { get; }

    /// <summary>
    /// 依回答產生漸進任務藍圖。回答已保證含全部問題鍵，但值仍需在此解析與驗證。
    /// </summary>
    /// <param name="answers">回答（鍵為問題 key）。</param>
    /// <param name="lengthDays">已驗證的目標天數。</param>
    /// <returns>任務藍圖，依顯示順序。</returns>
    /// <exception cref="DomainValidationException">回答格式或範圍不合法，或目標不比現況更好。</exception>
    public abstract IReadOnlyList<GoalQuestBlueprint> Build(IReadOnlyDictionary<string, string> answers, int lengthDays);

    /// <summary>「幾天內達成」這一題，所有類別共用。</summary>
    protected static GoalQuestion LengthDaysQuestion { get; } = new("lengthDays", "幾天內達成", GoalQuestionType.Integer, 7, 90, 30);

    /// <summary>
    /// 讀時間型回答。
    /// </summary>
    /// <param name="answers">回答。</param>
    /// <param name="key">鍵。</param>
    /// <returns>距中午的分鐘數。</returns>
    protected static int ReadTime(IReadOnlyDictionary<string, string> answers, string key)
    {
        return TimeOfDay.Parse(answers[key]);
    }

    /// <summary>
    /// 讀數字型回答並檢查範圍。
    /// </summary>
    /// <param name="answers">回答。</param>
    /// <param name="question">對應的問題（提供 Min／Max 與 Label）。</param>
    /// <returns>數值。</returns>
    /// <exception cref="DomainValidationException">不是數字或超出範圍。</exception>
    protected static decimal ReadNumber(IReadOnlyDictionary<string, string> answers, GoalQuestion question)
    {
        if (!decimal.TryParse(answers[question.Key], NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            || value < question.Min || value > question.Max)
        {
            throw new DomainValidationException("InvalidAnswer", $"「{question.Label}」須為 {question.Min} 到 {question.Max} 的數字");
        }

        return value;
    }

    /// <summary>
    /// 目標不比現況好時丟例外。
    /// </summary>
    /// <param name="isBetterOrEqual">目標是否比現況好或相等。</param>
    /// <param name="label">顯示用的項目名稱。</param>
    /// <exception cref="DomainValidationException">目標比現況差。</exception>
    protected static void RequireBetter(bool isBetterOrEqual, string label)
    {
        if (!isBetterOrEqual)
        {
            throw new DomainValidationException("TargetNotBetter", $"{label}的目標不能比現況差");
        }
    }

    /// <summary>
    /// 依起終點與天數算出藍圖的漸進參數。
    /// </summary>
    /// <param name="start">起點。</param>
    /// <param name="end">終點。</param>
    /// <param name="lengthDays">目標天數。</param>
    /// <param name="granularity">四捨五入單位。</param>
    /// <returns>(StepValue, StageCount, DaysPerStep)。</returns>
    protected static (decimal Step, int StageCount, int DaysPerStep) Schedule(decimal start, decimal end, int lengthDays, decimal granularity)
    {
        var stageCount = Progression.StageCountFor(lengthDays, Progression.DefaultDaysPerStep);
        return (Progression.StepFor(start, end, stageCount, granularity), stageCount, Progression.DefaultDaysPerStep);
    }
}
```

- [ ] **Step 4: 寫四個類別**

建立 `src/SoloLeveling.Domain/Goals/RoutineGoal.cs`：

```csharp
namespace SoloLeveling.Domain.Goals;

/// <summary>
/// 作息：就寢與起床時間各自逐階提早，每階四捨五入到 5 分鐘。
/// </summary>
public sealed class RoutineGoal : GoalCategoryDefinition
{
    private const decimal Granularity = 5;

    /// <summary>類別。</summary>
    public override GoalCategory Category => GoalCategory.Routine;

    /// <summary>顯示名稱。</summary>
    public override string Title => "作息";

    /// <summary>問題清單，最後一題是 <c>lengthDays</c>。</summary>
    public override IReadOnlyList<GoalQuestion> Questions { get; } =
    [
        new("currentBedtime", "現在大概幾點睡", GoalQuestionType.Time, null, null, null),
        new("targetBedtime", "希望幾點睡", GoalQuestionType.Time, null, null, null),
        new("currentWakeTime", "現在大概幾點起床", GoalQuestionType.Time, null, null, null),
        new("targetWakeTime", "希望幾點起床", GoalQuestionType.Time, null, null, null),
        LengthDaysQuestion,
    ];

    /// <summary>此類別會取代的基本任務索引（對應 <see cref="DefaultQuests.All"/>）。</summary>
    public override IReadOnlyList<int> ReplacesBasicQuestIndexes { get; } = [0, 1];

    /// <summary>
    /// 依回答產生漸進任務藍圖。
    /// </summary>
    /// <param name="answers">回答（鍵為問題 key）。</param>
    /// <param name="lengthDays">已驗證的目標天數。</param>
    /// <returns>任務藍圖，依顯示順序。</returns>
    /// <exception cref="DomainValidationException">回答格式或範圍不合法，或目標不比現況更好。</exception>
    public override IReadOnlyList<GoalQuestBlueprint> Build(IReadOnlyDictionary<string, string> answers, int lengthDays)
    {
        var bedStart = ReadTime(answers, "currentBedtime");
        var bedEnd = ReadTime(answers, "targetBedtime");
        var wakeStart = ReadTime(answers, "currentWakeTime");
        var wakeEnd = ReadTime(answers, "targetWakeTime");
        RequireBetter(bedEnd <= bedStart, "就寢時間");
        RequireBetter(wakeEnd <= wakeStart, "起床時間");

        var bed = Schedule(bedStart, bedEnd, lengthDays, Granularity);
        var wake = Schedule(wakeStart, wakeEnd, lengthDays, Granularity);
        return
        [
            new("{target} 前上床睡覺", StatType.Vitality, Difficulty.Normal, QuestType.Check, ProgressionValueKind.TimeOfDay,
                bedStart, bedEnd, bed.Step, bed.StageCount, bed.DaysPerStep, null, null),
            new("{target} 前起床", StatType.Vitality, Difficulty.Hard, QuestType.Check, ProgressionValueKind.TimeOfDay,
                wakeStart, wakeEnd, wake.Step, wake.StageCount, wake.DaysPerStep, null, null),
        ];
    }
}
```

建立 `src/SoloLeveling.Domain/Goals/ExerciseGoal.cs`：

```csharp
namespace SoloLeveling.Domain.Goals;

/// <summary>
/// 運動：每天分鐘數逐階增加。
/// </summary>
public sealed class ExerciseGoal : GoalCategoryDefinition
{
    private static readonly GoalQuestion Current = new("currentMinutes", "現在每天大概運動幾分鐘", GoalQuestionType.Integer, 0, 300, 0);
    private static readonly GoalQuestion Target = new("targetMinutes", "希望每天運動幾分鐘", GoalQuestionType.Integer, 0, 300, 30);

    /// <summary>類別。</summary>
    public override GoalCategory Category => GoalCategory.Exercise;

    /// <summary>顯示名稱。</summary>
    public override string Title => "運動";

    /// <summary>問題清單，最後一題是 <c>lengthDays</c>。</summary>
    public override IReadOnlyList<GoalQuestion> Questions { get; } = [Current, Target, LengthDaysQuestion];

    /// <summary>此類別會取代的基本任務索引（對應 <see cref="DefaultQuests.All"/>）。</summary>
    public override IReadOnlyList<int> ReplacesBasicQuestIndexes { get; } = [7];

    /// <summary>
    /// 依回答產生漸進任務藍圖。
    /// </summary>
    /// <param name="answers">回答（鍵為問題 key）。</param>
    /// <param name="lengthDays">已驗證的目標天數。</param>
    /// <returns>任務藍圖，依顯示順序。</returns>
    /// <exception cref="DomainValidationException">回答格式或範圍不合法，或目標不比現況更好。</exception>
    public override IReadOnlyList<GoalQuestBlueprint> Build(IReadOnlyDictionary<string, string> answers, int lengthDays)
    {
        var start = ReadNumber(answers, Current);
        var end = ReadNumber(answers, Target);
        RequireBetter(end >= start, "運動分鐘");
        var s = Schedule(start, end, lengthDays, 1);
        return [new("運動", StatType.Strength, Difficulty.Normal, QuestType.Count, ProgressionValueKind.Number, start, end, s.Step, s.StageCount, s.DaysPerStep, 5, "分鐘")];
    }
}
```

建立 `src/SoloLeveling.Domain/Goals/ReadingGoal.cs`：

```csharp
namespace SoloLeveling.Domain.Goals;

/// <summary>
/// 閱讀：每天分鐘數逐階增加。
/// </summary>
public sealed class ReadingGoal : GoalCategoryDefinition
{
    private static readonly GoalQuestion Current = new("currentMinutes", "現在每天大概閱讀幾分鐘", GoalQuestionType.Integer, 0, 300, 0);
    private static readonly GoalQuestion Target = new("targetMinutes", "希望每天閱讀幾分鐘", GoalQuestionType.Integer, 0, 300, 30);

    /// <summary>類別。</summary>
    public override GoalCategory Category => GoalCategory.Reading;

    /// <summary>顯示名稱。</summary>
    public override string Title => "閱讀";

    /// <summary>問題清單，最後一題是 <c>lengthDays</c>。</summary>
    public override IReadOnlyList<GoalQuestion> Questions { get; } = [Current, Target, LengthDaysQuestion];

    /// <summary>此類別會取代的基本任務索引（對應 <see cref="DefaultQuests.All"/>）。</summary>
    public override IReadOnlyList<int> ReplacesBasicQuestIndexes { get; } = [3];

    /// <summary>
    /// 依回答產生漸進任務藍圖。
    /// </summary>
    /// <param name="answers">回答（鍵為問題 key）。</param>
    /// <param name="lengthDays">已驗證的目標天數。</param>
    /// <returns>任務藍圖，依顯示順序。</returns>
    /// <exception cref="DomainValidationException">回答格式或範圍不合法，或目標不比現況更好。</exception>
    public override IReadOnlyList<GoalQuestBlueprint> Build(IReadOnlyDictionary<string, string> answers, int lengthDays)
    {
        var start = ReadNumber(answers, Current);
        var end = ReadNumber(answers, Target);
        RequireBetter(end >= start, "閱讀分鐘");
        var s = Schedule(start, end, lengthDays, 1);
        return [new("閱讀", StatType.Intelligence, Difficulty.Normal, QuestType.Count, ProgressionValueKind.Number, start, end, s.Step, s.StageCount, s.DaysPerStep, 5, "分鐘")];
    }
}
```

建立 `src/SoloLeveling.Domain/Goals/ScreenTimeGoal.cs`：

```csharp
namespace SoloLeveling.Domain.Goals;

/// <summary>
/// 螢幕時間：每天小時上限逐階降低，每階四捨五入到 0.25 小時。
/// </summary>
public sealed class ScreenTimeGoal : GoalCategoryDefinition
{
    private static readonly GoalQuestion Current = new("currentHours", "現在每天大概用手機幾小時", GoalQuestionType.Decimal, 0, 24, 4);
    private static readonly GoalQuestion Target = new("targetHours", "希望每天不超過幾小時", GoalQuestionType.Decimal, 0, 24, 2);

    /// <summary>類別。</summary>
    public override GoalCategory Category => GoalCategory.ScreenTime;

    /// <summary>顯示名稱。</summary>
    public override string Title => "螢幕時間";

    /// <summary>問題清單，最後一題是 <c>lengthDays</c>。</summary>
    public override IReadOnlyList<GoalQuestion> Questions { get; } = [Current, Target, LengthDaysQuestion];

    /// <summary>此類別會取代的基本任務索引（對應 <see cref="DefaultQuests.All"/>）。</summary>
    public override IReadOnlyList<int> ReplacesBasicQuestIndexes { get; } = [5];

    /// <summary>
    /// 依回答產生漸進任務藍圖。
    /// </summary>
    /// <param name="answers">回答（鍵為問題 key）。</param>
    /// <param name="lengthDays">已驗證的目標天數。</param>
    /// <returns>任務藍圖，依顯示順序。</returns>
    /// <exception cref="DomainValidationException">回答格式或範圍不合法，或目標不比現況更好。</exception>
    public override IReadOnlyList<GoalQuestBlueprint> Build(IReadOnlyDictionary<string, string> answers, int lengthDays)
    {
        var start = ReadNumber(answers, Current);
        var end = ReadNumber(answers, Target);
        RequireBetter(end <= start, "螢幕時間");
        var s = Schedule(start, end, lengthDays, 0.25m);
        return [new("手機螢幕時間", StatType.Willpower, Difficulty.Hard, QuestType.Limit, ProgressionValueKind.Number, start, end, s.Step, s.StageCount, s.DaysPerStep, 0.5m, "小時")];
    }
}
```

- [ ] **Step 5: 寫登記表與規劃器**

建立 `src/SoloLeveling.Domain/Goals/GoalCategories.cs`：

```csharp
namespace SoloLeveling.Domain.Goals;

/// <summary>
/// 所有目標類別的登記表；順序即前端顯示順序。
/// </summary>
public static class GoalCategories
{
    /// <summary>全部類別。</summary>
    public static IReadOnlyList<GoalCategoryDefinition> All { get; } =
    [
        new RoutineGoal(),
        new ExerciseGoal(),
        new ReadingGoal(),
        new ScreenTimeGoal(),
    ];

    /// <summary>
    /// 依 enum 取定義。
    /// </summary>
    /// <param name="category">類別。</param>
    /// <returns>定義。</returns>
    /// <exception cref="DomainValidationException">類別未登記。</exception>
    public static GoalCategoryDefinition Get(GoalCategory category)
    {
        return All.FirstOrDefault(c => c.Category == category)
            ?? throw new DomainValidationException("UnknownCategory", $"未知的目標類別 {category}");
    }
}
```

建立 `src/SoloLeveling.Domain/Goals/GoalPlanner.cs`：

```csharp
using System.Globalization;

namespace SoloLeveling.Domain.Goals;

/// <summary>
/// 依類別與回答產生漸進任務藍圖：檢查回答鍵齊全、驗證天數，再交給類別定義算出起終點與階段。
/// </summary>
public static class GoalPlanner
{
    /// <summary>目標天數下限。</summary>
    public const int MinLengthDays = 7;

    /// <summary>目標天數上限。</summary>
    public const int MaxLengthDays = 90;

    /// <summary>
    /// 規劃。
    /// </summary>
    /// <param name="category">類別。</param>
    /// <param name="answers">回答（鍵為問題 key，值為字串）。</param>
    /// <returns>任務藍圖。</returns>
    /// <exception cref="DomainValidationException">類別未知、缺回答、天數不合法、回答格式或範圍不合法、目標不比現況好。</exception>
    public static IReadOnlyList<GoalQuestBlueprint> Plan(GoalCategory category, IReadOnlyDictionary<string, string> answers)
    {
        var definition = GoalCategories.Get(category);
        foreach (var question in definition.Questions)
        {
            if (!answers.ContainsKey(question.Key))
            {
                throw new DomainValidationException("MissingAnswer", $"缺少「{question.Label}」的回答");
            }
        }

        return definition.Build(answers, LengthDaysOf(answers));
    }

    /// <summary>
    /// 讀並驗證 <c>lengthDays</c>。
    /// </summary>
    /// <param name="answers">回答。</param>
    /// <returns>7–90 的整數。</returns>
    /// <exception cref="DomainValidationException">缺少、不是整數或超出範圍。</exception>
    public static int LengthDaysOf(IReadOnlyDictionary<string, string> answers)
    {
        if (!answers.TryGetValue("lengthDays", out var text)
            || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days)
            || days < MinLengthDays || days > MaxLengthDays)
        {
            throw new DomainValidationException("InvalidLengthDays", $"目標天數須為 {MinLengthDays} 到 {MaxLengthDays} 的整數");
        }

        return days;
    }
}
```

- [ ] **Step 6: 跑測試確認通過**

Run: `dotnet test tests/SoloLeveling.Domain.Tests --filter "FullyQualifiedName~GoalPlannerTests"`
Expected: 12 個 PASS。

再跑：`dotnet build && dotnet format --verify-no-changes`
Expected: 0 錯誤、無格式差異。

- [ ] **Step 7: Commit**

```bash
git add src/SoloLeveling.Domain/Goals tests/SoloLeveling.Domain.Tests/GoalPlannerTests.cs
git commit -m "feat: 新增四個目標類別定義與漸進任務規劃器" -m "1. 每個類別自帶問題、驗證與公式，新增類別只需加一個定義
2. 規劃器統一檢查回答鍵與天數，再由類別算起終點、階數與每階變化量" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: 判定改用有效目標、DB 對應與 migration

**Files:**
- Modify: `src/SoloLeveling.Domain/Rules/ProgressUpdater.cs:22-56`
- Modify: `tests/SoloLeveling.Domain.Tests/ProgressUpdaterTests.cs`（既有呼叫補參數）
- Modify: `src/SoloLeveling.Infrastructure/AppDbContext.cs:21-31`、`:65-94`
- Create: `src/SoloLeveling.Infrastructure/Migrations/<timestamp>_AddGoalsAndProgression.cs`（由工具產生）
- Test: `tests/SoloLeveling.Domain.Tests/ProgressUpdaterTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `Progression.EffectiveTarget`、`QuestProgress.TargetSnapshot`、`Goal`。
- Produces: `ProgressUpdater.SetValue(Player player, DailyLog log, IReadOnlyList<Quest> activeQuests, Quest quest, decimal? value, DateTimeOffset now, int doneDaysBeforeToday)`；`AppDbContext.Goals: DbSet<Goal>`；migration `AddGoalsAndProgression`。

- [ ] **Step 1: 看既有 ProgressUpdaterTests 怎麼呼叫 SetValue**

Run: `grep -n "SetValue(" tests/SoloLeveling.Domain.Tests/ProgressUpdaterTests.cs | head`
Expected: 列出多處 `ProgressUpdater.SetValue(player, log, quests, quest, value, now)` 之類的呼叫。

- [ ] **Step 2: 寫失敗的測試**

在 `tests/SoloLeveling.Domain.Tests/ProgressUpdaterTests.cs` 類別內加（沿用該檔既有的 helper 建 Player／DailyLog；若沒有現成的，照下面自建）：

```csharp
    [Fact]
    public void SetValue_Count漸進任務_以當階目標判定並寫入快照()
    {
        var player = new Player { UserId = Guid.NewGuid(), Level = 1, Xp = 0 };
        var log = new DailyLog { Id = Guid.NewGuid(), UserId = player.UserId, Date = new DateOnly(2026, 9, 28) };
        var quest = new Quest
        {
            Id = Guid.NewGuid(),
            UserId = player.UserId,
            Name = "閱讀",
            QuestType = QuestType.Count,
            Difficulty = Difficulty.Normal,
            StatType = StatType.Intelligence,
            GoalId = Guid.NewGuid(),
            ValueKind = ProgressionValueKind.Number,
            StartValue = 10,
            EndValue = 30,
            StepValue = 2,
            StageCount = 10,
            DaysPerStep = 3,
        };
        var now = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);

        // 第 1 階目標 12：填 11 未完成
        ProgressUpdater.SetValue(player, log, [quest], quest, 11, now, doneDaysBeforeToday: 0);
        var progress = log.Progresses.Single();
        progress.IsDone.Should().BeFalse();
        progress.TargetSnapshot.Should().Be(12);

        // 填 12 完成
        ProgressUpdater.SetValue(player, log, [quest], quest, 12, now, doneDaysBeforeToday: 0);
        progress.IsDone.Should().BeTrue();
        player.Xp.Should().Be(20);

        // 已達標 3 天 → 第 2 階目標 14：12 不再算完成
        var log2 = new DailyLog { Id = Guid.NewGuid(), UserId = player.UserId, Date = new DateOnly(2026, 10, 1) };
        ProgressUpdater.SetValue(player, log2, [quest], quest, 12, now, doneDaysBeforeToday: 3);
        log2.Progresses.Single().IsDone.Should().BeFalse();
        log2.Progresses.Single().TargetSnapshot.Should().Be(14);
    }

    [Fact]
    public void SetValue_一般任務_快照等於TargetValue()
    {
        var player = new Player { UserId = Guid.NewGuid(), Level = 1, Xp = 0 };
        var log = new DailyLog { Id = Guid.NewGuid(), UserId = player.UserId, Date = new DateOnly(2026, 9, 28) };
        var quest = new Quest { Id = Guid.NewGuid(), UserId = player.UserId, Name = "喝水", QuestType = QuestType.Count, Difficulty = Difficulty.Easy, StatType = StatType.Vitality, TargetValue = 8 };
        var now = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);

        ProgressUpdater.SetValue(player, log, [quest], quest, 8, now, doneDaysBeforeToday: 0);

        log.Progresses.Single().TargetSnapshot.Should().Be(8);
    }
```

- [ ] **Step 3: 跑測試確認失敗**

Run: `dotnet test tests/SoloLeveling.Domain.Tests --filter "FullyQualifiedName~ProgressUpdaterTests"`
Expected: 建置失敗，`SetValue` 沒有 `doneDaysBeforeToday` 參數。

- [ ] **Step 4: 改 ProgressUpdater.SetValue**

把 `src/SoloLeveling.Domain/Rules/ProgressUpdater.cs` 的 `SetValue` 改成：

```csharp
    /// <summary>
    /// 對某任務寫入新的進度值（規格 4.4）。漸進任務以當階目標判定（見 <see cref="Progression.EffectiveTarget"/>），並把判定用的目標寫入 <see cref="QuestProgress.TargetSnapshot"/>。
    /// </summary>
    /// <param name="player">玩家。</param>
    /// <param name="log">今日紀錄（含 <see cref="DailyLog.Progresses"/>）。</param>
    /// <param name="activeQuests">今日未封存的任務，用來算達標率分母。</param>
    /// <param name="quest">要寫入的任務。</param>
    /// <param name="value">新的進度值；null 表示未填。</param>
    /// <param name="now">當下時間（UTC）。</param>
    /// <param name="doneDaysBeforeToday">此任務在今天之前的達標天數；一般任務傳 0 即可。</param>
    /// <returns>這次產生的 EXP 事件。</returns>
    /// <exception cref="DomainValidationException">Check 類型的值不是 0／1，或 Count／Limit 的值為負。</exception>
    public static IReadOnlyList<XpEvent> SetValue(
        Player player,
        DailyLog log,
        IReadOnlyList<Quest> activeQuests,
        Quest quest,
        decimal? value,
        DateTimeOffset now,
        int doneDaysBeforeToday)
    {
        ValidateValue(quest, value);

        var events = new List<XpEvent>();
        var progress = log.Progresses.FirstOrDefault(p => p.QuestId == quest.Id);
        if (progress == null)
        {
            progress = new QuestProgress { Id = Guid.NewGuid(), DailyLogId = log.Id, QuestId = quest.Id, CreatedAt = now };
            log.Progresses.Add(progress);
        }

        var target = Progression.EffectiveTarget(quest, doneDaysBeforeToday);
        var wasDone = progress.IsDone;
        var isDone = CompletionRules.IsDone(quest.QuestType, value, target);
        progress.Value = value;
        progress.IsDone = isDone;
        progress.TargetSnapshot = target;

        if (!wasDone && isDone)
        {
            events.Add(Grant(player, progress, quest, now));
        }
        else if (wasDone && !isDone)
        {
            events.Add(Revoke(player, progress, quest, now));
        }

        events.AddRange(Recalculate(player, log, activeQuests, now));
        return events;
    }
```

既有 `ProgressUpdaterTests` 裡所有 `SetValue(...)` 呼叫在最後補 `, 0`（或 `doneDaysBeforeToday: 0`）。

- [ ] **Step 5: 跑 Domain 測試確認通過**

Run: `dotnet test tests/SoloLeveling.Domain.Tests`
Expected: 全部 PASS（既有 68 個加本計畫新增的）。此時 `SoloLeveling.Api` 建置會失敗（`TodayService` 呼叫舊簽章），Task 5 修，先只跑 Domain.Tests。

- [ ] **Step 6: DbContext 對應**

`src/SoloLeveling.Infrastructure/AppDbContext.cs` 在 `Quests` 屬性之後加：

```csharp

    /// <summary>引導式目標。</summary>
    public DbSet<Goal> Goals => Set<Goal>();
```

`Quest` 的設定區塊（`b.HasIndex(x => new { x.UserId, x.IsArchived });` 之前）加：

```csharp
            b.Property(x => x.ValueKind).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.StartValue).HasPrecision(10, 2);
            b.Property(x => x.EndValue).HasPrecision(10, 2);
            b.Property(x => x.StepValue).HasPrecision(10, 2);
            b.HasOne<Goal>().WithMany().HasForeignKey(x => x.GoalId);
```

`QuestProgress` 區塊加：

```csharp
            b.Property(x => x.TargetSnapshot).HasPrecision(10, 2);
```

`QuestProgress` 區塊之前新增：

```csharp
        modelBuilder.Entity<Goal>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            b.Property(x => x.Category).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.Answers).HasColumnType("jsonb").IsRequired();
            b.HasIndex(x => new { x.UserId, x.IsArchived });
            // 同類別同時只能有一個進行中的目標
            b.HasIndex(x => new { x.UserId, x.Category }).IsUnique().HasFilter("\"IsArchived\" = false");
        });
```

- [ ] **Step 7: 讓 Api 暫時可建置，再產 migration**

`dotnet ef` 需要 Api 專案能建置。先把 `src/SoloLeveling.Api/Services/TodayService.cs:50` 的 `SetValue(...)` 呼叫最後補 `, 0`（Task 5 會換成真正的達標天數）。

Run:

```bash
dotnet build
dotnet ef migrations add AddGoalsAndProgression -p src/SoloLeveling.Infrastructure -s src/SoloLeveling.Api -o Migrations
```

Expected: `Migrations/` 多出 `<timestamp>_AddGoalsAndProgression.cs` 與 `.Designer.cs`，Snapshot 更新。打開 migration 檔確認 `Up` 有 `CreateTable("Goals")`、`AddColumn` 七個 Quest 欄位、`AddColumn("TargetSnapshot")`、兩個 Goals 索引（其中一個 `filter: "\"IsArchived\" = false"`）。

- [ ] **Step 8: 跑全部測試**

Run: `dotnet test`
Expected: Domain 與 Api 全部 PASS（Api 測試會跑新 migration，證明 migration 可套用）。

Run: `dotnet format --verify-no-changes`
Expected: 無差異（`.editorconfig` 對 `Migrations/` 關閉 analyzer）。

- [ ] **Step 9: Commit**

```bash
git add src/SoloLeveling.Domain/Rules/ProgressUpdater.cs tests/SoloLeveling.Domain.Tests/ProgressUpdaterTests.cs src/SoloLeveling.Infrastructure src/SoloLeveling.Api/Services/TodayService.cs
git commit -m "feat: 進度判定改用當階目標並新增 Goals 與漸進欄位的 migration" -m "1. SetValue 以 Progression.EffectiveTarget 判定並寫入 TargetSnapshot，歷史查得到當天目標
2. Goals 表加 partial unique index，同類別同時只有一個進行中的目標" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: Goals API：類別、預覽、建立、列表、封存；註冊不再建預設任務

**Files:**
- Create: `src/SoloLeveling.Api/Contracts/GoalDtos.cs`
- Create: `src/SoloLeveling.Api/Services/GoalService.cs`
- Create: `src/SoloLeveling.Api/Controllers/GoalsController.cs`
- Modify: `src/SoloLeveling.Api/Program.cs:38`（DI 註冊）
- Modify: `src/SoloLeveling.Api/Services/AccountService.cs:25-26`、`:71-84`
- Modify: `src/SoloLeveling.Api/Services/TodayContextLoader.cs`（`TodayContext` 加 `DoneDaysBeforeToday`）
- Modify: `src/SoloLeveling.Api/Contracts/Dtos.cs:71`（`MeResponse` 加 `NeedsOnboarding`）
- Modify: `src/SoloLeveling.Api/Services/PlayerService.cs:72-76`
- Modify: `tests/SoloLeveling.Api.Tests/ApiFactory.cs:38-54`
- Test: `tests/SoloLeveling.Api.Tests/GoalsApiTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `Goal`、`Progression.*`；Task 2 的 `GoalCategories`、`GoalPlanner`、`GoalQuestBlueprint`；Task 3 的 `AppDbContext.Goals`。
- Produces:
  - `TodayContext(User User, Player Player, DailyLog TodayLog, List<Quest> ActiveQuests, DateOnly Today, IReadOnlyDictionary<Guid, int> DoneDaysBeforeToday)`；`TodayContext.DoneDaysOf(Guid questId) → int`
  - `MeResponse(UserDto User, PlayerDto Player, ProgramDto Program, bool NeedsOnboarding)`
  - DTO：`CategoriesResponse`、`GoalInput`、`CreateGoalsRequest`、`PreviewResponse`、`GoalsResponse`、`GoalDto`、`GoalQuestDto`
  - `GoalService.Categories()`、`Preview(CreateGoalsRequest)`、`CreateAsync(Guid userId, CreateGoalsRequest, ct)`、`ListAsync(Guid userId, ct)`、`ArchiveAsync(Guid userId, Guid goalId, ct)`
  - 端點 `GET /goals/categories`、`POST /goals/preview`、`POST /goals`、`GET /goals`、`DELETE /goals/{id}`
  - `ApiFactory.RegisterAsync(string timeZoneId = "UTC", bool seedBasicQuests = true)`

- [ ] **Step 1: 寫失敗的整合測試**

建立 `tests/SoloLeveling.Api.Tests/GoalsApiTests.cs`：

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace SoloLeveling.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class GoalsApiTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture.ConnectionString);

    public void Dispose()
    {
        _factory.Dispose();
    }

    private static object RoutineGoal(string bed = "01:00", string targetBed = "00:00", string wake = "08:00", string targetWake = "07:00", int days = 30)
    {
        return new
        {
            category = "Routine",
            answers = new { currentBedtime = bed, targetBedtime = targetBed, currentWakeTime = wake, targetWakeTime = targetWake, lengthDays = days },
        };
    }

    private static object ReadingGoal(int current = 10, int target = 30, int days = 30)
    {
        return new { category = "Reading", answers = new { currentMinutes = current, targetMinutes = target, lengthDays = days } };
    }

    [Fact]
    public async Task Register_不帶基本任務_needsOnboarding為true且今日無任務()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        var today = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");

        me.GetProperty("needsOnboarding").GetBoolean().Should().BeTrue();
        today.GetProperty("quests").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Register_預設帶基本任務_needsOnboarding為false且有9個任務()
    {
        var client = await _factory.RegisterAsync();

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        var today = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");

        me.GetProperty("needsOnboarding").GetBoolean().Should().BeFalse();
        today.GetProperty("quests").GetArrayLength().Should().Be(9);
        today.GetProperty("quests")[0].GetProperty("name").GetString().Should().Be("23:30 前上床睡覺");
    }

    [Fact]
    public async Task GetCategories_回四個類別與九個基本任務()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);

        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/goals/categories");

        var categories = body.GetProperty("categories");
        categories.GetArrayLength().Should().Be(4);
        categories[0].GetProperty("category").GetString().Should().Be("Routine");
        categories[0].GetProperty("title").GetString().Should().Be("作息");
        categories[0].GetProperty("questions").EnumerateArray().Last().GetProperty("key").GetString().Should().Be("lengthDays");
        categories[0].GetProperty("replacesBasicQuestIndexes").EnumerateArray().Select(e => e.GetInt32()).Should().Equal(0, 1);
        body.GetProperty("basicQuests").GetArrayLength().Should().Be(9);
        body.GetProperty("basicQuests")[2].GetProperty("name").GetString().Should().Be("喝水");
    }

    [Fact]
    public async Task PostPreview_回階段摘要且不寫入()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);

        var response = await client.PostAsJsonAsync("/api/v1/goals/preview", new { goals = new[] { RoutineGoal() }, basicQuestIndexes = Array.Empty<int>() });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var quest = body.GetProperty("goals")[0].GetProperty("quests")[0];
        quest.GetProperty("name").GetString().Should().Be("00:55 前上床睡覺");
        quest.GetProperty("startLabel").GetString().Should().Be("01:00");
        quest.GetProperty("endLabel").GetString().Should().Be("00:00");
        quest.GetProperty("stageCount").GetInt32().Should().Be(10);
        quest.GetProperty("daysPerStep").GetInt32().Should().Be(3);
        quest.GetProperty("stepLabel").GetString().Should().Be("提早 5 分鐘");
        var today = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");
        today.GetProperty("quests").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task PostGoals_建立作息與閱讀_今日出現任務且needsOnboarding變false()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);

        var response = await client.PostAsJsonAsync("/api/v1/goals", new { goals = new[] { RoutineGoal(), ReadingGoal() }, basicQuestIndexes = new[] { 2, 8 } });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var goals = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("goals");
        goals.GetArrayLength().Should().Be(2);
        goals[0].GetProperty("category").GetString().Should().Be("Routine");
        goals[0].GetProperty("quests")[0].GetProperty("stage").GetInt32().Should().Be(1);
        goals[0].GetProperty("quests")[0].GetProperty("stageCount").GetInt32().Should().Be(10);

        var today = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");
        var names = today.GetProperty("quests").EnumerateArray().Select(q => q.GetProperty("name").GetString()).ToList();
        names.Should().Equal("00:55 前上床睡覺", "07:55 前起床", "閱讀", "喝水", "寫下今天的三件好事");
        var reading = today.GetProperty("quests")[2];
        reading.GetProperty("targetValue").GetDecimal().Should().Be(12);
        reading.GetProperty("progression").GetProperty("targetLabel").GetString().Should().Be("12 分鐘");
        today.GetProperty("quests")[3].GetProperty("progression").ValueKind.Should().Be(JsonValueKind.Null);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        me.GetProperty("needsOnboarding").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task PostGoals_基本任務含被取代的索引_回400()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);

        var response = await client.PostAsJsonAsync("/api/v1/goals", new { goals = new[] { RoutineGoal() }, basicQuestIndexes = new[] { 0 } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString().Should().Be("BasicQuestReplaced");
    }

    [Fact]
    public async Task PostGoals_同類別已有進行中_回409()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);
        await client.PostAsJsonAsync("/api/v1/goals", new { goals = new[] { ReadingGoal() }, basicQuestIndexes = Array.Empty<int>() });

        var response = await client.PostAsJsonAsync("/api/v1/goals", new { goals = new[] { ReadingGoal() }, basicQuestIndexes = Array.Empty<int>() });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString().Should().Be("GoalAlreadyActive");
    }

    [Fact]
    public async Task PostGoals_回答格式錯_回400()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);

        var response = await client.PostAsJsonAsync("/api/v1/goals", new
        {
            goals = new[] { new { category = "Routine", answers = new { currentBedtime = "1:00", targetBedtime = "00:00", currentWakeTime = "08:00", targetWakeTime = "07:00", lengthDays = "abc" } } },
            basicQuestIndexes = Array.Empty<int>(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var code = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString();
        code.Should().BeOneOf("InvalidLengthDays", "InvalidTime");
    }

    [Fact]
    public async Task PostGoals_未知類別_回400()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);

        var response = await client.PostAsJsonAsync("/api/v1/goals", new { goals = new[] { new { category = "Cooking", answers = new { lengthDays = 30 } } }, basicQuestIndexes = Array.Empty<int>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteGoal_封存後任務消失且同類別可重建()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);
        var created = await (await client.PostAsJsonAsync("/api/v1/goals", new { goals = new[] { ReadingGoal() }, basicQuestIndexes = new[] { 2 } })).Content.ReadFromJsonAsync<JsonElement>();
        var goalId = created.GetProperty("goals")[0].GetProperty("id").GetGuid();

        var del = await client.DeleteAsync($"/api/v1/goals/{goalId}");

        del.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var today = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");
        today.GetProperty("quests").EnumerateArray().Select(q => q.GetProperty("name").GetString()).Should().Equal("喝水");
        var goals = await client.GetFromJsonAsync<JsonElement>("/api/v1/goals");
        goals.GetProperty("goals").GetArrayLength().Should().Be(0);

        var again = await client.PostAsJsonAsync("/api/v1/goals", new { goals = new[] { ReadingGoal() }, basicQuestIndexes = Array.Empty<int>() });
        again.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task DeleteGoal_不存在_回404()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);

        var del = await client.DeleteAsync($"/api/v1/goals/{Guid.NewGuid()}");

        del.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/SoloLeveling.Api.Tests --filter "FullyQualifiedName~GoalsApiTests"`
Expected: 建置失敗，`RegisterAsync` 沒有 `seedBasicQuests` 參數。

- [ ] **Step 3: TodayContext 加達標天數**

把 `src/SoloLeveling.Api/Services/TodayContextLoader.cs` 整檔改成：

```csharp
using Microsoft.EntityFrameworkCore;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Services;

/// <summary>
/// 結算後的「今日」工作內容；所有實體皆由 DbContext 追蹤，修改後 SaveChanges 即可。
/// </summary>
/// <param name="User">使用者。</param>
/// <param name="Player">玩家。</param>
/// <param name="TodayLog">今日紀錄，<see cref="DailyLog.Progresses"/> 已載入。</param>
/// <param name="ActiveQuests">未封存任務，依 SortOrder 排序。</param>
/// <param name="Today">使用者時區的今日。</param>
/// <param name="DoneDaysBeforeToday">每個漸進任務在今天之前的達標天數；一般任務不在字典內。</param>
public sealed record TodayContext(
    User User,
    Player Player,
    DailyLog TodayLog,
    List<Quest> ActiveQuests,
    DateOnly Today,
    IReadOnlyDictionary<Guid, int> DoneDaysBeforeToday)
{
    /// <summary>
    /// 某任務在今天之前的達標天數；不在字典內（一般任務或新任務）回 0。
    /// </summary>
    /// <param name="questId">任務 ID。</param>
    /// <returns>達標天數。</returns>
    public int DoneDaysOf(Guid questId)
    {
        return DoneDaysBeforeToday.TryGetValue(questId, out var days) ? days : 0;
    }
}

/// <summary>
/// 先結算、再把今日需要的實體一次載入。必須在呼叫端開啟的交易內使用，結算會沿用該交易。
/// </summary>
/// <param name="db">DbContext。</param>
/// <param name="settlement">結算服務。</param>
public class TodayContextLoader(AppDbContext db, SettlementService settlement)
{
    /// <summary>
    /// 結算並載入今日內容。漸進任務的達標天數一次批次查出，不逐任務查。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>今日內容。</returns>
    public async Task<TodayContext> LoadAsync(Guid userId, CancellationToken ct)
    {
        var settled = await settlement.SettleAsync(userId, ct);
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        var player = await db.Players.SingleAsync(p => p.UserId == userId, ct);
        // 今日紀錄已被追蹤，查出的進度會由 EF 自動掛回 Progresses
        await db.QuestProgresses.Where(p => p.DailyLogId == settled.TodayLog.Id).LoadAsync(ct);
        var activeQuests = await db.Quests
            .Where(q => q.UserId == userId && !q.IsArchived)
            .OrderBy(q => q.SortOrder)
            .ToListAsync(ct);

        var progressionIds = activeQuests.Where(q => q.GoalId != null).Select(q => q.Id).ToList();
        var doneDays = progressionIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await db.QuestProgresses
                .Join(db.DailyLogs, p => p.DailyLogId, l => l.Id, (p, l) => new { p.QuestId, p.IsDone, l.Date })
                .Where(x => x.IsDone && x.Date < settled.Today && progressionIds.Contains(x.QuestId))
                .GroupBy(x => x.QuestId)
                .Select(g => new { QuestId = g.Key, Days = g.Count() })
                .ToDictionaryAsync(x => x.QuestId, x => x.Days, ct);

        return new TodayContext(user, player, settled.TodayLog, activeQuests, settled.Today, doneDays);
    }
}
```

- [ ] **Step 4: MeResponse 加 needsOnboarding**

`src/SoloLeveling.Api/Contracts/Dtos.cs` 的 `MeResponse` 改成：

```csharp
/// <summary>玩家總覽。</summary>
/// <param name="User">使用者。</param>
/// <param name="Player">玩家狀態。</param>
/// <param name="Program">66 天計畫。</param>
/// <param name="NeedsOnboarding">是否需要引導（沒有任何未封存任務）。</param>
public record MeResponse(UserDto User, PlayerDto Player, ProgramDto Program, bool NeedsOnboarding);
```

`src/SoloLeveling.Api/Services/PlayerService.cs` 的 `BuildAsync` 回傳改成：

```csharp
        return new MeResponse(context.User.ToDto(), context.Player.ToDto(context.TodayLog.IsCleared), program.ToDto(context.Today), context.ActiveQuests.Count == 0);
```

- [ ] **Step 5: 註冊不再建預設任務**

`src/SoloLeveling.Api/Services/AccountService.cs`：把 `RegisterAsync` 的 summary 改成「註冊：建立 User、Player 與第 1 個 66 天週期；任務由引導流程（POST /goals）建立。」，刪掉 `db.Quests.AddRange(DefaultQuests.All.Select(...))` 整段（`:71-84`）。若 `using SoloLeveling.Domain;` 因此變成未使用，`dotnet format` 會要求移除，一併移除。

- [ ] **Step 6: DTO**

建立 `src/SoloLeveling.Api/Contracts/GoalDtos.cs`：

```csharp
using System.Text.Json;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Goals;

namespace SoloLeveling.Api.Contracts;

/// <summary>引導問題。</summary>
/// <param name="Key">回答的鍵。</param>
/// <param name="Label">顯示文字。</param>
/// <param name="Type">回答型別（time／integer／decimal）。</param>
/// <param name="Min">最小值。</param>
/// <param name="Max">最大值。</param>
/// <param name="Default">預設值。</param>
public record GoalQuestionDto(string Key, string Label, GoalQuestionType Type, decimal? Min, decimal? Max, decimal? Default);

/// <summary>目標類別定義。</summary>
/// <param name="Category">類別。</param>
/// <param name="Title">顯示名稱。</param>
/// <param name="Questions">問題清單。</param>
/// <param name="ReplacesBasicQuestIndexes">會取代的基本任務索引。</param>
public record GoalCategoryDto(GoalCategory Category, string Title, List<GoalQuestionDto> Questions, List<int> ReplacesBasicQuestIndexes);

/// <summary>基本任務（原預設任務）。</summary>
/// <param name="Index">索引，對應 basicQuestIndexes。</param>
/// <param name="Name">名稱。</param>
/// <param name="StatType">屬性代碼。</param>
/// <param name="Difficulty">難度。</param>
public record BasicQuestDto(int Index, string Name, StatType StatType, Difficulty Difficulty);

/// <summary>GET /goals/categories。</summary>
/// <param name="Categories">類別定義。</param>
/// <param name="BasicQuests">基本任務。</param>
public record CategoriesResponse(List<GoalCategoryDto> Categories, List<BasicQuestDto> BasicQuests);

/// <summary>一個目標的輸入。</summary>
/// <param name="Category">類別。</param>
/// <param name="Answers">回答；值可為字串或數字，統一轉成字串交給 Domain 解析。</param>
public record GoalInput(GoalCategory Category, Dictionary<string, JsonElement> Answers)
{
    /// <summary>
    /// 把回答的 JSON 值轉成字串字典（數字取原始文字、字串取內容）。
    /// </summary>
    /// <returns>字串字典。</returns>
    public IReadOnlyDictionary<string, string> AnswersAsStrings()
    {
        return Answers.ToDictionary(kv => kv.Key, kv => kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() ?? string.Empty : kv.Value.GetRawText());
    }
}

/// <summary>POST /goals 與 POST /goals/preview 的輸入。</summary>
/// <param name="Goals">目標，至少一個，類別不可重複。</param>
/// <param name="BasicQuestIndexes">要一併加入的基本任務索引；可為 null 或空。</param>
public record CreateGoalsRequest(List<GoalInput> Goals, List<int>? BasicQuestIndexes);

/// <summary>預覽中的單一任務。</summary>
/// <param name="Name">第 1 階的顯示名稱。</param>
/// <param name="QuestType">任務類型。</param>
/// <param name="StatType">屬性代碼。</param>
/// <param name="Difficulty">難度。</param>
/// <param name="StartLabel">起點顯示文字。</param>
/// <param name="EndLabel">終點顯示文字。</param>
/// <param name="StageCount">總階數。</param>
/// <param name="DaysPerStep">每階達標天數。</param>
/// <param name="StepLabel">每階變化的顯示文字，例如「提早 5 分鐘」。</param>
public record PreviewQuestDto(string Name, QuestType QuestType, StatType StatType, Difficulty Difficulty, string StartLabel, string EndLabel, int StageCount, int DaysPerStep, string StepLabel);

/// <summary>預覽中的單一目標。</summary>
/// <param name="Category">類別。</param>
/// <param name="Title">顯示名稱。</param>
/// <param name="Quests">會產生的任務。</param>
public record GoalPreviewDto(GoalCategory Category, string Title, List<PreviewQuestDto> Quests);

/// <summary>POST /goals/preview。</summary>
/// <param name="Goals">預覽結果。</param>
public record PreviewResponse(List<GoalPreviewDto> Goals);

/// <summary>目標底下的任務。</summary>
/// <param name="Id">任務 ID。</param>
/// <param name="Name">今日的顯示名稱。</param>
/// <param name="Stage">目前階段。</param>
/// <param name="StageCount">總階數。</param>
/// <param name="TargetLabel">今日目標的顯示文字。</param>
/// <param name="IsArchived">是否已單獨封存。</param>
public record GoalQuestDto(Guid Id, string Name, int Stage, int StageCount, string TargetLabel, bool IsArchived);

/// <summary>目標。</summary>
/// <param name="Id">目標 ID。</param>
/// <param name="Category">類別。</param>
/// <param name="Title">顯示名稱。</param>
/// <param name="LengthDays">目標天數。</param>
/// <param name="StartDate">開始日。</param>
/// <param name="Quests">任務。</param>
public record GoalDto(Guid Id, GoalCategory Category, string Title, int LengthDays, DateOnly StartDate, List<GoalQuestDto> Quests);

/// <summary>GET /goals 與 POST /goals 的回應。</summary>
/// <param name="Goals">進行中的目標。</param>
public record GoalsResponse(List<GoalDto> Goals);
```

- [ ] **Step 7: GoalService**

建立 `src/SoloLeveling.Api/Services/GoalService.cs`：

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Errors;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Goals;
using SoloLeveling.Domain.Rules;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Services;

/// <summary>
/// 引導式目標：類別定義、預覽、建立（連同基本任務）、列表與封存。建立與封存會改變今日達標率分母，因此在結算後的今日內容上操作並重算。
/// </summary>
/// <param name="db">DbContext。</param>
/// <param name="loader">今日內容載入（含結算）。</param>
/// <param name="clock">時間來源。</param>
public class GoalService(AppDbContext db, TodayContextLoader loader, TimeProvider clock)
{
    /// <summary>
    /// GET /goals/categories：類別定義與基本任務清單。
    /// </summary>
    /// <returns>定義。</returns>
    public static CategoriesResponse Categories()
    {
        var categories = GoalCategories.All.Select(c => new GoalCategoryDto(
            c.Category,
            c.Title,
            c.Questions.Select(q => new GoalQuestionDto(q.Key, q.Label, q.Type, q.Min, q.Max, q.Default)).ToList(),
            c.ReplacesBasicQuestIndexes.ToList())).ToList();
        var basics = DefaultQuests.All.Select((t, i) => new BasicQuestDto(i, t.Name, t.StatType, t.Difficulty)).ToList();
        return new CategoriesResponse(categories, basics);
    }

    /// <summary>
    /// POST /goals/preview：只計算不寫入。
    /// </summary>
    /// <param name="request">輸入。</param>
    /// <returns>每個目標會產生的任務與階段摘要。</returns>
    /// <exception cref="ApiErrorException">類別重複或基本任務索引不合法（400）。</exception>
    public static PreviewResponse Preview(CreateGoalsRequest request)
    {
        var plans = PlanAll(request);
        return new PreviewResponse(plans.Select(p => new GoalPreviewDto(
            p.Definition.Category,
            p.Definition.Title,
            p.Blueprints.Select(ToPreview).ToList())).ToList());
    }

    /// <summary>
    /// POST /goals：建立目標、其漸進任務與勾選的基本任務。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="request">輸入。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>建立後的進行中目標。</returns>
    /// <exception cref="ApiErrorException">輸入不合法（400）或同類別已有進行中的目標（409）。</exception>
    public async Task<GoalsResponse> CreateAsync(Guid userId, CreateGoalsRequest request, CancellationToken ct)
    {
        var plans = PlanAll(request);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        var now = clock.GetUtcNow();

        var activeCategories = await db.Goals.Where(g => g.UserId == userId && !g.IsArchived).Select(g => g.Category).ToListAsync(ct);
        var clash = plans.Select(p => p.Definition.Category).FirstOrDefault(activeCategories.Contains);
        if (clash != default)
        {
            throw ApiErrorException.Conflict("GoalAlreadyActive", $"「{GoalCategories.Get(clash).Title}」已有進行中的目標");
        }

        var sortOrder = context.ActiveQuests.Count == 0 ? 0 : context.ActiveQuests.Max(q => q.SortOrder) + 1;
        foreach (var plan in plans)
        {
            var goal = new Goal
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Category = plan.Definition.Category,
                Answers = JsonSerializer.Serialize(plan.Answers),
                LengthDays = plan.LengthDays,
                StartDate = context.Today,
                CreatedAt = now,
            };
            db.Goals.Add(goal);
            foreach (var b in plan.Blueprints)
            {
                var quest = new Quest
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Name = b.Name,
                    StatType = b.StatType,
                    Difficulty = b.Difficulty,
                    QuestType = b.QuestType,
                    Step = b.UiStep,
                    Unit = b.Unit,
                    SortOrder = sortOrder++,
                    CreatedAt = now,
                    GoalId = goal.Id,
                    ValueKind = b.ValueKind,
                    StartValue = b.StartValue,
                    EndValue = b.EndValue,
                    StepValue = b.StepValue,
                    StageCount = b.StageCount,
                    DaysPerStep = b.DaysPerStep,
                };
                db.Quests.Add(quest);
                context.ActiveQuests.Add(quest);
            }
        }

        foreach (var index in request.BasicQuestIndexes ?? [])
        {
            var t = DefaultQuests.All[index];
            var quest = new Quest
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = t.Name,
                StatType = t.StatType,
                Difficulty = t.Difficulty,
                QuestType = t.QuestType,
                TargetValue = t.TargetValue,
                Step = t.Step,
                Unit = t.Unit,
                SortOrder = sortOrder++,
                CreatedAt = now,
            };
            db.Quests.Add(quest);
            context.ActiveQuests.Add(quest);
        }

        db.XpEvents.AddRange(ProgressUpdater.Recalculate(context.Player, context.TodayLog, context.ActiveQuests, now));
        await db.SaveChangesAsync(ct);
        var response = await BuildListAsync(userId, context, ct);
        await tx.CommitAsync(ct);
        return response;
    }

    /// <summary>
    /// GET /goals：進行中的目標與各任務的當前階段。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>目標清單。</returns>
    public async Task<GoalsResponse> ListAsync(Guid userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        var response = await BuildListAsync(userId, context, ct);
        await tx.CommitAsync(ct);
        return response;
    }

    /// <summary>
    /// DELETE /goals/{id}：封存目標與其未封存任務並重算今日達標。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="goalId">目標 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>非同步作業。</returns>
    /// <exception cref="ApiErrorException">目標不存在或已封存（404）。</exception>
    public async Task ArchiveAsync(Guid userId, Guid goalId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        var goal = await db.Goals.SingleOrDefaultAsync(g => g.Id == goalId && g.UserId == userId && !g.IsArchived, ct)
            ?? throw ApiErrorException.NotFound("GoalNotFound", "目標不存在");
        var now = clock.GetUtcNow();

        goal.IsArchived = true;
        goal.ArchivedAt = now;
        foreach (var quest in context.ActiveQuests.Where(q => q.GoalId == goalId).ToList())
        {
            quest.IsArchived = true;
            quest.ArchivedAt = now;
            context.ActiveQuests.Remove(quest);
        }

        db.XpEvents.AddRange(ProgressUpdater.Recalculate(context.Player, context.TodayLog, context.ActiveQuests, now));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private sealed record GoalPlan(GoalCategoryDefinition Definition, IReadOnlyDictionary<string, string> Answers, int LengthDays, IReadOnlyList<GoalQuestBlueprint> Blueprints);

    private static List<GoalPlan> PlanAll(CreateGoalsRequest request)
    {
        if (request.Goals.Count == 0)
        {
            throw ApiErrorException.BadRequest("NoGoals", "至少要選一個目標");
        }

        if (request.Goals.Select(g => g.Category).Distinct().Count() != request.Goals.Count)
        {
            throw ApiErrorException.BadRequest("DuplicateCategory", "同一類別只能出現一次");
        }

        var plans = request.Goals.Select(g =>
        {
            var answers = g.AnswersAsStrings();
            var definition = GoalCategories.Get(g.Category);
            return new GoalPlan(definition, answers, GoalPlanner.LengthDaysOf(answers), GoalPlanner.Plan(g.Category, answers));
        }).ToList();

        var indexes = request.BasicQuestIndexes ?? [];
        if (indexes.Any(i => i < 0 || i >= DefaultQuests.All.Count) || indexes.Distinct().Count() != indexes.Count)
        {
            throw ApiErrorException.BadRequest("InvalidBasicQuestIndex", "basicQuestIndexes 不合法");
        }

        var replaced = plans.SelectMany(p => p.Definition.ReplacesBasicQuestIndexes).ToHashSet();
        if (indexes.Any(replaced.Contains))
        {
            throw ApiErrorException.BadRequest("BasicQuestReplaced", "選了會被目標取代的基本任務");
        }

        return plans;
    }

    private static PreviewQuestDto ToPreview(GoalQuestBlueprint b)
    {
        var probe = new Quest
        {
            Name = b.Name,
            QuestType = b.QuestType,
            Unit = b.Unit,
            GoalId = Guid.Empty,
            ValueKind = b.ValueKind,
            StartValue = b.StartValue,
            EndValue = b.EndValue,
            StepValue = b.StepValue,
            StageCount = b.StageCount,
            DaysPerStep = b.DaysPerStep,
        };
        var magnitude = Math.Abs(b.StepValue);
        var stepLabel = b.StepValue == 0
            ? "維持"
            : b.ValueKind == ProgressionValueKind.TimeOfDay
                ? $"提早 {magnitude:0.##} 分鐘"
                : $"{(b.StepValue > 0 ? "增加" : "減少")} {Progression.TargetLabel(probe, magnitude)}";
        return new PreviewQuestDto(
            Progression.RenderName(probe, 0),
            b.QuestType,
            b.StatType,
            b.Difficulty,
            Progression.TargetLabel(probe, b.StartValue),
            Progression.TargetLabel(probe, b.EndValue),
            b.StageCount,
            b.DaysPerStep,
            stepLabel);
    }

    private async Task<GoalsResponse> BuildListAsync(Guid userId, TodayContext context, CancellationToken ct)
    {
        var goals = await db.Goals.Where(g => g.UserId == userId && !g.IsArchived).OrderBy(g => g.CreatedAt).ToListAsync(ct);
        var goalIds = goals.Select(g => g.Id).ToHashSet();
        var quests = await db.Quests.Where(q => q.GoalId != null && goalIds.Contains(q.GoalId.Value)).OrderBy(q => q.SortOrder).ToListAsync(ct);
        var byGoal = quests.ToLookup(q => q.GoalId!.Value);

        return new GoalsResponse(goals.Select(g => new GoalDto(
            g.Id,
            g.Category,
            GoalCategories.Get(g.Category).Title,
            g.LengthDays,
            g.StartDate,
            byGoal[g.Id].Select(q =>
            {
                var days = context.DoneDaysOf(q.Id);
                var target = Progression.EffectiveTarget(q, days) ?? 0;
                return new GoalQuestDto(q.Id, Progression.RenderName(q, days), Progression.StageOf(q, days), q.StageCount ?? 1, Progression.TargetLabel(q, target), q.IsArchived);
            }).ToList())).ToList());
    }
}
```

- [ ] **Step 8: Controller 與 DI**

建立 `src/SoloLeveling.Api/Controllers/GoalsController.cs`：

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoloLeveling.Api.Auth;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Services;

namespace SoloLeveling.Api.Controllers;

/// <summary>
/// 引導式目標。
/// </summary>
/// <param name="goals">目標服務。</param>
[ApiController]
[Authorize]
[Route("api/v1/goals")]
public class GoalsController(GoalService goals) : ControllerBase
{
    /// <summary>
    /// 類別定義（問題清單）與基本任務清單，供引導流程畫表單。
    /// </summary>
    /// <returns>定義。</returns>
    [HttpGet("categories")]
    [ProducesResponseType<CategoriesResponse>(StatusCodes.Status200OK)]
    public ActionResult<CategoriesResponse> Categories()
    {
        return GoalService.Categories();
    }

    /// <summary>
    /// 預覽會產生的任務與階段摘要，不寫入。
    /// </summary>
    /// <param name="request">目標與回答。</param>
    /// <returns>預覽。</returns>
    [HttpPost("preview")]
    [ProducesResponseType<PreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public ActionResult<PreviewResponse> Preview(CreateGoalsRequest request)
    {
        return GoalService.Preview(request);
    }

    /// <summary>
    /// 建立目標、其漸進任務與勾選的基本任務。
    /// </summary>
    /// <param name="request">目標與回答。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>201 與進行中的目標。</returns>
    [HttpPost]
    [ProducesResponseType<GoalsResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GoalsResponse>> Create(CreateGoalsRequest request, CancellationToken ct)
    {
        var created = await goals.CreateAsync(User.GetUserId(), request, ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>
    /// 進行中的目標與各任務的當前階段。
    /// </summary>
    /// <param name="ct">取消權杖。</param>
    /// <returns>目標清單。</returns>
    [HttpGet]
    [ProducesResponseType<GoalsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GoalsResponse>> List(CancellationToken ct)
    {
        return await goals.ListAsync(User.GetUserId(), ct);
    }

    /// <summary>
    /// 封存目標與其任務。
    /// </summary>
    /// <param name="id">目標 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>204。</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await goals.ArchiveAsync(User.GetUserId(), id, ct);
        return NoContent();
    }
}
```

`src/SoloLeveling.Api/Program.cs` 在 `builder.Services.AddScoped<ProgramService>();` 之後加：

```csharp
builder.Services.AddScoped<GoalService>();
```

- [ ] **Step 9: ApiFactory 帶基本任務**

`tests/SoloLeveling.Api.Tests/ApiFactory.cs` 的 `RegisterAsync` 改成：

```csharp
    /// <summary>
    /// 註冊一個隨機 Email 的新使用者並回傳帶 Bearer 的 client。
    /// 預設會透過 POST /goals 帶入全部 9 個基本任務，維持「註冊即有 9 個任務」的既有測試前提；要測引導流程時傳 <paramref name="seedBasicQuests"/> = false。
    /// </summary>
    public async Task<HttpClient> RegisterAsync(string timeZoneId = "UTC", bool seedBasicQuests = true)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = $"{Guid.NewGuid():N}@test.local",
            password = "password123",
            displayName = "tester",
            timeZoneId,
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        if (seedBasicQuests)
        {
            var seeded = await client.PostAsJsonAsync("/api/v1/goals", new { goals = Array.Empty<object>(), basicQuestIndexes = Enumerable.Range(0, 9).ToArray() });
            seeded.EnsureSuccessStatusCode();
        }

        return client;
    }
```

注意：`PlanAll` 對 `Goals.Count == 0` 會丟 `NoGoals`。為了讓「只帶基本任務」合法，把 `PlanAll` 的檢查改成「`Goals` 為空且 `BasicQuestIndexes` 也為空才丟 `NoGoals`」：

```csharp
        if (request.Goals.Count == 0 && (request.BasicQuestIndexes ?? []).Count == 0)
        {
            throw ApiErrorException.BadRequest("NoGoals", "至少要選一個目標或基本任務");
        }
```

`TodayService.Build`（Task 5 才會加 `progression`）：本 Task 的測試 `PostGoals_建立作息與閱讀_…` 需要 `today` 的 `name` 已渲染、`targetValue` 為有效目標、`progression` 欄位存在。因此本 Task 先把 `TodayService.Build` 改成用 `Progression` 渲染，並在 `TodayQuestDto` 加 `Progression` 欄位；`SetProgressAsync` 的達標天數也一併接上（取代 Task 3 暫放的 `0`）：

`src/SoloLeveling.Api/Contracts/TodayDtos.cs` 加：

```csharp
/// <summary>漸進任務的階段資訊；一般任務為 null。</summary>
/// <param name="GoalId">所屬目標。</param>
/// <param name="Stage">目前階段（從 1 起）。</param>
/// <param name="StageCount">總階數。</param>
/// <param name="TargetLabel">今日目標的顯示文字。</param>
public record ProgressionDto(Guid GoalId, int Stage, int StageCount, string TargetLabel);
```

`TodayQuestDto` 最後加參數 `ProgressionDto? Progression`（並在 XML 註解加 `<param name="Progression">漸進任務的階段資訊；一般任務為 null。</param>`）。

`src/SoloLeveling.Api/Services/TodayService.cs`：`SetProgressAsync` 的 `SetValue(...)` 最後一個參數改成 `context.DoneDaysOf(quest.Id)`；`Build` 改成：

```csharp
    private static TodayResponse Build(TodayContext context)
    {
        var progressByQuest = context.TodayLog.Progresses.ToDictionary(p => p.QuestId);
        var quests = context.ActiveQuests.Select(q =>
        {
            progressByQuest.TryGetValue(q.Id, out var progress);
            var reward = CompletionRules.RewardOf(q.Difficulty);
            var days = context.DoneDaysOf(q.Id);
            var target = Progression.EffectiveTarget(q, days);
            var progression = Progression.IsProgression(q)
                ? new ProgressionDto(q.GoalId!.Value, Progression.StageOf(q, days), q.StageCount ?? 1, Progression.TargetLabel(q, target ?? 0))
                : null;
            return new TodayQuestDto(
                q.Id, Progression.RenderName(q, days), q.StatType, q.Difficulty, q.QuestType,
                q.QuestType == QuestType.Check ? null : target, q.Step, q.Unit, q.SortOrder,
                progress?.Value, progress?.IsDone ?? false, reward.Xp, reward.Stat, progression);
        }).ToList();
        // 以下不變
```

`TodayService.cs` 加 `using SoloLeveling.Domain;`。

- [ ] **Step 10: 跑測試確認通過**

Run: `dotnet test tests/SoloLeveling.Api.Tests --filter "FullyQualifiedName~GoalsApiTests"`
Expected: 11 個 PASS。

Run: `dotnet test`
Expected: 全部 PASS。既有 `TodayApiTests`、`QuestsApiTests`、`AccountApiTests` 因 `RegisterAsync` 預設帶基本任務而不受影響；若有測試斷言「註冊回應後立刻有任務」以外的行為改變，依失敗訊息調整該測試，並在 commit body 說明。

Run: `dotnet format --verify-no-changes`
Expected: 無差異。

- [ ] **Step 11: Commit**

```bash
git add src/SoloLeveling.Api tests/SoloLeveling.Api.Tests/ApiFactory.cs tests/SoloLeveling.Api.Tests/GoalsApiTests.cs
git commit -m "feat: 新增目標 API 並改由引導流程建立任務" -m "1. 註冊不再自動建 9 個任務，改由 POST /goals 依回答產生漸進任務並可勾選基本任務
2. 今日內容一次批次載入漸進任務的達標天數，今日 API 回渲染後的名稱、當階目標與階段
3. GET /me 加 needsOnboarding 供前端導向引導流程" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: 階段推進整合測試與漸進任務的編輯限制

**Files:**
- Modify: `src/SoloLeveling.Api/Services/QuestService.cs:73-92`
- Modify: `src/SoloLeveling.Api/Contracts/Dtos.cs:99`（`QuestDto` 加 `GoalId`）、`Mappers.cs:23-26`
- Test: `tests/SoloLeveling.Api.Tests/ProgressionApiTests.cs`

**Interfaces:**
- Consumes: Task 4 的 `/goals` 端點、`TodayContext.DoneDaysOf`、`ApiFactory.Clock`。
- Produces: `QuestDto(..., int SortOrder, Guid? GoalId)`；`PUT /quests/{id}` 對漸進任務回 400 `ProgressionQuestLocked`。

- [ ] **Step 1: 寫失敗的測試**

建立 `tests/SoloLeveling.Api.Tests/ProgressionApiTests.cs`：

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace SoloLeveling.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ProgressionApiTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture.ConnectionString);

    public void Dispose()
    {
        _factory.Dispose();
    }

    private async Task<(HttpClient Client, Guid BedId, Guid ReadingId)> SetupAsync()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);
        var response = await client.PostAsJsonAsync("/api/v1/goals", new
        {
            goals = new object[]
            {
                new { category = "Routine", answers = new { currentBedtime = "01:00", targetBedtime = "00:00", currentWakeTime = "08:00", targetWakeTime = "08:00", lengthDays = 30 } },
                new { category = "Reading", answers = new { currentMinutes = 10, targetMinutes = 30, lengthDays = 30 } },
            },
            basicQuestIndexes = Array.Empty<int>(),
        });
        response.EnsureSuccessStatusCode();
        var today = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");
        var quests = today.GetProperty("quests").EnumerateArray().ToList();
        return (client, quests[0].GetProperty("id").GetGuid(), quests[2].GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> QuestOfAsync(HttpClient client, Guid id)
    {
        var today = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");
        return today.GetProperty("quests").EnumerateArray().Single(q => q.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task 連續達標3天後_第4天升到第2階_中間漏一天則不升()
    {
        var (client, bedId, _) = await SetupAsync();

        // 第 1、2、3 天各勾一次
        for (var day = 0; day < 3; day++)
        {
            (await QuestOfAsync(client, bedId)).GetProperty("name").GetString().Should().Be("00:55 前上床睡覺");
            await client.PutAsJsonAsync($"/api/v1/today/quests/{bedId}/progress", new { value = 1 });
            _factory.Clock.Advance(TimeSpan.FromDays(1));
        }

        // 第 4 天：第 2 階
        var day4 = await QuestOfAsync(client, bedId);
        day4.GetProperty("name").GetString().Should().Be("00:50 前上床睡覺");
        day4.GetProperty("progression").GetProperty("stage").GetInt32().Should().Be(2);

        // 第 4、5 天沒勾，第 6 天勾 → 達標 4 天，仍第 2 階
        _factory.Clock.Advance(TimeSpan.FromDays(2));
        await client.PutAsJsonAsync($"/api/v1/today/quests/{bedId}/progress", new { value = 1 });
        _factory.Clock.Advance(TimeSpan.FromDays(1));
        var day7 = await QuestOfAsync(client, bedId);
        day7.GetProperty("progression").GetProperty("stage").GetInt32().Should().Be(2);
        day7.GetProperty("name").GetString().Should().Be("00:50 前上床睡覺");

        // 再達標 2 天 → 6 天 → 第 3 階 00:45
        for (var i = 0; i < 2; i++)
        {
            await client.PutAsJsonAsync($"/api/v1/today/quests/{bedId}/progress", new { value = 1 });
            _factory.Clock.Advance(TimeSpan.FromDays(1));
        }

        (await QuestOfAsync(client, bedId)).GetProperty("name").GetString().Should().Be("00:45 前上床睡覺");
    }

    [Fact]
    public async Task 今天自己的完成_不影響今天的目標()
    {
        var (client, bedId, _) = await SetupAsync();
        await client.PutAsJsonAsync($"/api/v1/today/quests/{bedId}/progress", new { value = 1 });
        await client.PutAsJsonAsync($"/api/v1/today/quests/{bedId}/progress", new { value = 0 });
        await client.PutAsJsonAsync($"/api/v1/today/quests/{bedId}/progress", new { value = 1 });

        (await QuestOfAsync(client, bedId)).GetProperty("progression").GetProperty("stage").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task SetProgress_Count漸進任務_以當階目標判定並寫入快照()
    {
        var (client, _, readingId) = await SetupAsync();

        var r11 = await (await client.PutAsJsonAsync($"/api/v1/today/quests/{readingId}/progress", new { value = 11 })).Content.ReadFromJsonAsync<JsonElement>();
        r11.GetProperty("quests").EnumerateArray().Single(q => q.GetProperty("id").GetGuid() == readingId).GetProperty("isDone").GetBoolean().Should().BeFalse();

        var r12 = await (await client.PutAsJsonAsync($"/api/v1/today/quests/{readingId}/progress", new { value = 12 })).Content.ReadFromJsonAsync<JsonElement>();
        r12.GetProperty("quests").EnumerateArray().Single(q => q.GetProperty("id").GetGuid() == readingId).GetProperty("isDone").GetBoolean().Should().BeTrue();

        await using var db = fixture.CreateDbContext();
        var progress = db.QuestProgresses.Single(p => p.QuestId == readingId);
        progress.TargetSnapshot.Should().Be(12);
    }

    [Fact]
    public async Task 到最後一階後_目標固定在終點()
    {
        var (client, bedId, _) = await SetupAsync();
        for (var day = 0; day < 40; day++)
        {
            await client.PutAsJsonAsync($"/api/v1/today/quests/{bedId}/progress", new { value = 1 });
            _factory.Clock.Advance(TimeSpan.FromDays(1));
        }

        var quest = await QuestOfAsync(client, bedId);
        quest.GetProperty("name").GetString().Should().Be("00:00 前上床睡覺");
        quest.GetProperty("progression").GetProperty("stage").GetInt32().Should().Be(10);
        quest.GetProperty("progression").GetProperty("stageCount").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task PutQuest_漸進任務改目標欄位_回400_改難度成功()
    {
        var (client, _, readingId) = await SetupAsync();
        var quests = await client.GetFromJsonAsync<JsonElement>("/api/v1/quests");
        var reading = quests.EnumerateArray().Single(q => q.GetProperty("id").GetGuid() == readingId);
        reading.GetProperty("goalId").ValueKind.Should().NotBe(JsonValueKind.Null);

        var locked = await client.PutAsJsonAsync($"/api/v1/quests/{readingId}", new { name = "閱讀", statType = "INT", difficulty = "Normal", questType = "Count", targetValue = 99, step = 5, unit = "分鐘" });
        locked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await locked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString().Should().Be("ProgressionQuestLocked");

        var ok = await client.PutAsJsonAsync($"/api/v1/quests/{readingId}", new { name = "閱讀", statType = "INT", difficulty = "Hard", questType = "Count", targetValue = (decimal?)null, step = 5, unit = "分鐘" });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("difficulty").GetString().Should().Be("Hard");
    }

    [Fact]
    public async Task DeleteQuest_單獨封存漸進任務_目標仍在()
    {
        var (client, bedId, _) = await SetupAsync();

        (await client.DeleteAsync($"/api/v1/quests/{bedId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var goals = await client.GetFromJsonAsync<JsonElement>("/api/v1/goals");
        var routine = goals.GetProperty("goals").EnumerateArray().Single(g => g.GetProperty("category").GetString() == "Routine");
        routine.GetProperty("quests").EnumerateArray().Single(q => q.GetProperty("id").GetGuid() == bedId).GetProperty("isArchived").GetBoolean().Should().BeTrue();
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/SoloLeveling.Api.Tests --filter "FullyQualifiedName~ProgressionApiTests"`
Expected: `PutQuest_…` 失敗（沒有 `goalId`、沒有 400）；其他四個若 Task 4 實作正確應 PASS。若階段推進的測試失敗，先確認 `TodayContextLoader` 的 `Date < settled.Today` 過濾與 `SetProgressAsync` 傳入的達標天數。

注意 `QuestService.Validate` 會擋「Count 類型 `targetValue` 不是 > 0」，而漸進 Count 任務的 `TargetValue` 是 null。Step 3 要在 `UpdateAsync` 對漸進任務跳過該檢查。

- [ ] **Step 3: QuestDto 加 GoalId、QuestService 鎖定**

`src/SoloLeveling.Api/Contracts/Dtos.cs` 的 `QuestDto` 改成：

```csharp
/// <summary>任務。</summary>
/// <param name="Id">任務 ID。</param>
/// <param name="Name">名稱；漸進的時間類任務為含 <c>{target}</c> 的樣板，今日 API 才會渲染。</param>
/// <param name="StatType">屬性代碼。</param>
/// <param name="Difficulty">難度。</param>
/// <param name="QuestType">任務類型。</param>
/// <param name="TargetValue">目標值；漸進任務為 null。</param>
/// <param name="Step">增減量。</param>
/// <param name="Unit">單位。</param>
/// <param name="SortOrder">顯示順序。</param>
/// <param name="GoalId">所屬目標；一般任務為 null。</param>
public record QuestDto(Guid Id, string Name, StatType StatType, Difficulty Difficulty, QuestType QuestType, decimal? TargetValue, decimal? Step, string? Unit, int SortOrder, Guid? GoalId);
```

`Mappers.ToDto(Quest)` 最後多傳 `quest.GoalId`。

`src/SoloLeveling.Api/Services/QuestService.cs` 的 `UpdateAsync` 改成：

```csharp
    public async Task<QuestDto> UpdateAsync(Guid userId, Guid questId, QuestRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        var quest = FindActive(context, questId);
        var now = clock.GetUtcNow();

        if (Progression.IsProgression(quest))
        {
            // 漸進任務的目標由公式決定，只開放屬性與難度
            var unchanged = request.Name.Trim() == quest.Name
                && request.QuestType == quest.QuestType
                && request.TargetValue == quest.TargetValue
                && request.Step == quest.Step
                && request.Unit?.Trim() == quest.Unit;
            if (!unchanged)
            {
                throw ApiErrorException.BadRequest("ProgressionQuestLocked", "漸進任務只能修改屬性與難度；要改目標請封存目標後重新建立");
            }

            quest.StatType = request.StatType;
            quest.Difficulty = request.Difficulty;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return quest.ToDto();
        }

        Validate(request);
        if (quest.QuestType != request.QuestType)
        {
            db.XpEvents.AddRange(ProgressUpdater.ClearQuestProgress(context.Player, context.TodayLog, context.ActiveQuests, quest, now));
            db.QuestProgresses.RemoveRange(db.QuestProgresses.Local.Where(p => p.QuestId == quest.Id && p.DailyLogId == context.TodayLog.Id));
        }

        Apply(quest, request);
        db.XpEvents.AddRange(ProgressUpdater.Recalculate(context.Player, context.TodayLog, context.ActiveQuests, now));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return quest.ToDto();
    }
```

`UpdateAsync` 的 XML 註解補一句「漸進任務只能改屬性與難度，其餘欄位不同回 400」，`<exception>` 加 `ProgressionQuestLocked`。

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test`
Expected: 全部 PASS。

Run: `dotnet format --verify-no-changes`
Expected: 無差異。

- [ ] **Step 5: Commit**

```bash
git add src/SoloLeveling.Api tests/SoloLeveling.Api.Tests/ProgressionApiTests.cs
git commit -m "feat: 漸進任務鎖定目標欄位並補階段推進的整合測試" -m "1. 漸進任務的目標由公式決定，PUT /quests 只開放屬性與難度，避免手動值與公式打架
2. 以假時鐘驗證連續達標升階、漏一天停留、最後一階固定終點" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: 前端：引導流程、今日階段標示、設定頁目標區塊

**Files:**
- Modify: `src/SoloLeveling.Api/wwwroot/app.js`（`state`、`renderToday` 的任務列、`renderSettings`、`route`；新增 `renderOnboarding`）
- Modify: `src/SoloLeveling.Api/wwwroot/app.css`（少量樣式）

**Interfaces:**
- Consumes: `GET /me` 的 `needsOnboarding`；`GET /goals/categories`；`POST /goals/preview`；`POST /goals`；`GET /goals`；`DELETE /goals/{id}`；`GET /today` 任務的 `progression`；`GET /quests` 的 `goalId`。
- Produces: 路由 `#onboarding`；沒有自動化測試，以瀏覽器手動驗證。

- [ ] **Step 1: 路由與導向**

`app.js` 的 `route()` 改成：

```js
  async function route() {
    const hash = (location.hash || '#today').slice(1);
    if (!state.token) {
      renderLogin(hash === 'register' ? 'register' : 'login');
      return;
    }
    if (hash === 'login' || hash === 'register') {
      location.hash = '#today';
      return;
    }
    window.scrollTo(0, 0);
    try {
      await loadToday();
      // 沒有任何任務就強制走引導；引導完成前不開放其他頁
      if (state.me.needsOnboarding && hash !== 'onboarding') {
        location.hash = '#onboarding';
        return;
      }
      if (hash === 'onboarding') {
        $nav.classList.add('hidden');
        await renderOnboarding({ single: false });
        return;
      }
      $nav.classList.remove('hidden');
      $nav.querySelectorAll('a').forEach((a) => a.classList.toggle('active', a.dataset.route === hash));
      if (hash === 'progress') await renderProgress();
      else if (hash === 'settings') await renderSettings();
      else renderToday();
    } catch (err) {
      if (state.token) toast(err.message);
    }
  }
```

`renderLogin` 內註冊／登入成功後原本 `location.hash = '#today'` 不用改，`route()` 會依 `needsOnboarding` 轉到引導。

- [ ] **Step 2: 引導畫面**

在 `/* ---------- progress ---------- */` 之前加：

```js
  /* ---------- onboarding ---------- */
  const CATEGORY_HINT = {
    Routine: '早睡早起，每 3 天提早幾分鐘',
    Exercise: '每天運動的分鐘數逐步增加',
    Reading: '每天閱讀的分鐘數逐步增加',
    ScreenTime: '每天手機使用上限逐步降低',
  };

  function questionInput(q) {
    if (q.type === 'Time') return `<input type="time" name="${q.key}" required>`;
    const step = q.type === 'Integer' ? 1 : 0.25;
    return `<input type="number" name="${q.key}" required min="${num(q.min)}" max="${num(q.max)}" step="${step}" value="${num(q.default)}">`;
  }

  // single=true 時只做一個類別（設定頁「新增目標」），不顯示基本任務；excluded 為已有進行中的類別
  async function renderOnboarding({ single, excluded = [] }) {
    renderHeader();
    const defs = await api('GET', '/goals/categories');
    const categories = defs.categories.filter((c) => !excluded.includes(c.category));
    let step = 1;
    let chosen = [];
    const answers = {};

    const renderStep1 = () => {
      $view.innerHTML = `
        <div class="card">
          <h2>${single ? '新增目標' : '先設定你的目標'}</h2>
          <p class="sub">選擇想改善的項目，系統會依你的現況安排每天的任務，並逐步逼近目標。</p>
          <div class="choices">${categories.map((c) => `
            <label class="choice"><input type="${single ? 'radio' : 'checkbox'}" name="cat" value="${c.category}"><div><b>${h(c.title)}</b><div class="sub">${h(CATEGORY_HINT[c.category] ?? '')}</div></div></label>`).join('')}</div>
          <div class="actions"><button id="next" class="small primary">下一步</button></div>
        </div>`;
      $view.querySelector('#next').addEventListener('click', () => {
        chosen = [...$view.querySelectorAll('input[name=cat]:checked')].map((el) => categories.find((c) => c.category === el.value));
        if (!chosen.length) { toast('至少選一個目標'); return; }
        step = 2;
        renderStep2();
      });
    };

    const renderStep2 = () => {
      $view.innerHTML = `
        <div class="card">
          <h2>回答幾個問題</h2>
          <form id="qa">${chosen.map((c) => `
            <div class="section-title">${h(c.title)}</div>
            ${c.questions.map((q) => `<label class="field">${h(q.label)}${questionInput(q).replace('name="', `name="${c.category}.`)}</label>`).join('')}`).join('')}
            <div class="actions"><button type="button" class="small back">上一步</button><button type="submit" class="small primary">預覽計畫</button></div>
          </form>
        </div>`;
      $view.querySelector('.back').addEventListener('click', () => { step = 1; renderStep1(); });
      $view.querySelector('#qa').addEventListener('submit', async (e) => {
        e.preventDefault();
        const form = e.target;
        chosen.forEach((c) => {
          answers[c.category] = Object.fromEntries(c.questions.map((q) => [q.key, form[`${c.category}.${q.key}`].value]));
        });
        try {
          const preview = await api('POST', '/goals/preview', { goals: goalsBody(), basicQuestIndexes: [] });
          step = 3;
          renderStep3(preview, defs.basicQuests);
        } catch (err) {
          toast(err.message);
        }
      });
    };

    const goalsBody = () => chosen.map((c) => ({ category: c.category, answers: answers[c.category] }));

    const renderStep3 = (preview, basics) => {
      const replaced = new Set(chosen.flatMap((c) => c.replacesBasicQuestIndexes));
      $view.innerHTML = `
        <div class="card">
          <h2>你的計畫</h2>
          ${preview.goals.map((g) => `
            <div class="section-title">${h(g.title)}</div>
            ${g.quests.map((q) => `
              <div class="quest"><div class="name">${h(q.name)}<span class="sub">${h(q.startLabel)} → ${h(q.endLabel)}，共 ${q.stageCount} 階，每 ${q.daysPerStep} 天達標就${h(q.stepLabel)}</span></div></div>`).join('')}`).join('')}
        </div>
        ${single ? '' : `
        <div class="card">
          <h2>基本任務</h2>
          <p class="sub">也可以一併加入這些日常任務，被目標取代的已排除。</p>
          ${basics.map((b) => `<label class="choice"><input type="checkbox" name="basic" value="${b.index}" ${replaced.has(b.index) ? 'disabled' : 'checked'}><div>${h(b.name)}<span class="badge">${b.statType}</span></div></label>`).join('')}
        </div>`}
        <div class="actions"><button class="small back">上一步</button><button id="confirm" class="small primary">開始</button></div>`;
      $view.querySelector('.back').addEventListener('click', () => { step = 2; renderStep2(); });
      $view.querySelector('#confirm').addEventListener('click', async () => {
        const basicQuestIndexes = single ? [] : [...$view.querySelectorAll('input[name=basic]:checked')].map((el) => Number(el.value));
        try {
          await api('POST', '/goals', { goals: goalsBody(), basicQuestIndexes });
          toast('計畫已建立', true);
          if (single) { await renderSettings(); } else { location.hash = '#today'; }
        } catch (err) {
          toast(err.message);
        }
      });
    };

    renderStep1();
  }
```

- [ ] **Step 3: 今日頁顯示階段**

`renderToday` 內任務列的 `<div class="name">…</div>` 改成：

```js
              <div class="name">${h(q.name)}${q.progression ? `<span class="stage">第 ${q.progression.stage}／${q.progression.stageCount} 階</span>` : ''}<span class="sub">${h(q.difficulty)} · +${q.xpReward} EXP · +${q.statReward} ${s}</span></div>
```

- [ ] **Step 4: 設定頁目標區塊**

`renderSettings` 開頭改成同時取目標：

```js
    const [quests, goalsRes] = await Promise.all([api('GET', '/quests'), api('GET', '/goals')]);
    const goals = goalsRes.goals;
    const activeCategories = goals.map((g) => g.category);
```

在困難模式卡片之後、任務卡片之前插入：

```js
      <div class="card">
        <h2>目標</h2>
        ${goals.length ? goals.map((g) => `
          <div class="goal" data-id="${g.id}">
            <div class="name"><b>${h(g.title)}</b><span class="sub">自 ${h(g.startDate)} 起，${g.lengthDays} 天</span>
              ${g.quests.map((q) => `<div class="sub">${q.isArchived ? '（已封存）' : ''}${h(q.name)} · 第 ${q.stage}／${q.stageCount} 階</div>`).join('')}
            </div>
            <button class="small danger archive-goal">封存</button>
          </div>`).join('') : '<div class="sub">還沒有目標</div>'}
        <div class="actions"><button id="add-goal" class="small primary">＋ 新增目標</button></div>
      </div>
```

任務卡片的清單改成只列一般任務：

```js
        <div id="quest-list">${quests.filter((q) => !q.goalId).map((q) => `
```

事件綁定區加：

```js
    $view.querySelectorAll('.goal .archive-goal').forEach((b) => armConfirm(b, '封存', () => api('DELETE', `/goals/${b.closest('.goal').dataset.id}`)));
    $view.querySelector('#add-goal').addEventListener('click', () => renderOnboarding({ single: true, excluded: activeCategories }));
```

- [ ] **Step 5: 樣式**

`app.css` 檔尾加：

```css
.choices { display: flex; flex-direction: column; gap: 8px; margin: 12px 0; }
.choice { display: flex; gap: 10px; align-items: flex-start; padding: 10px; border: 1px solid var(--border); border-radius: 8px; cursor: pointer; }
.choice input { margin-top: 3px; }
.stage { display: inline-block; margin-left: 6px; font-size: 11px; color: var(--accent); }
.goal { display: flex; justify-content: space-between; align-items: flex-start; gap: 8px; padding: 8px 0; border-bottom: 1px solid var(--border); }
```

若 `app.css` 的變數不叫 `--border`／`--accent`，先 `grep -n '^  --' src/SoloLeveling.Api/wwwroot/app.css` 看實際名稱替換。

- [ ] **Step 6: 手動驗證**

```bash
docker compose up -d --build
```

在瀏覽器 http://localhost:8080 ，用新 Email 註冊，逐項確認：
- 註冊後自動進到引導頁，底部導覽隱藏；手動改網址到 `#today` 會被導回。
- 步驟 1 勾「作息」與「閱讀」→ 步驟 2 出現時間與數字輸入，天數預設 30 → 預覽顯示「01:00 → 00:00，共 10 階，每 3 天達標就提早 5 分鐘」之類摘要，基本任務清單裡「23:30 前上床睡覺」「7:30 前起床」「閱讀」被停用。
- 按「開始」→ 進今日頁，任務名為「00:55 前上床睡覺」並帶「第 1／10 階」。
- 設定頁看到目標區塊列出兩個目標與階段；任務清單不含目標任務；「新增目標」只列剩下兩個類別；封存目標後任務消失。
- 用既有帳號（之前註冊、有 9 個任務的）登入，不會進引導。

若 compose 內的資料庫還有舊帳號，migration 會自動套用，舊任務 `GoalId` 為 null，行為不變。

- [ ] **Step 7: Commit**

```bash
git add src/SoloLeveling.Api/wwwroot
git commit -m "feat: 前端加入引導流程、今日階段標示與設定頁目標區塊" -m "1. 沒有任務的帳號登入後強制走三步引導：選類別、回答、預覽並勾基本任務
2. 今日頁顯示漸進任務的階段，設定頁可查看與封存目標並新增單一目標" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: 文件

**Files:**
- Modify: `docs/SPEC.md`（§1、§4.10、新增 §4.11、§5、§6、§8、§11）
- Modify: `docs/ARCHITECTURE.md`（資料模型、請求生命週期、Api 檔案表）
- Modify: `README.md`（curl 範例）
- Modify: `CLAUDE.md`（關鍵不變式、專案結構、目前狀態）

**Interfaces:**
- Consumes: Task 1 到 6 的最終行為。
- Produces: 文件與程式碼一致。

- [ ] **Step 1: SPEC.md**

依 `docs/SPEC.md` 既有格式（用 `grep -n '^## \|^### ' docs/SPEC.md` 看章節），做以下修改：
- §1 交付範圍：加一條「註冊後由引導流程依固定類別產生漸進式任務」；「不做」保留，另加「AI 產生計畫」。
- §4.10 標題改「基本任務（原預設任務）」，內文說明改為「引導流程最後一步可勾選加入，被目標取代者不可勾」。
- 新增 §4.11「引導式目標與漸進任務」，內容照規格文件第 1 到 3 節濃縮：類別表、時間編碼、`StageCount`／`StepValue` 公式、`Stage`／`EffectiveTarget` 公式與「不越過終點、最後一階等於終點」、達標天數不需連續、今天的完成不影響今天的目標、`TargetSnapshot`。
- §5 資料模型：加 `Goal` 實體與 `Quest` 七個漸進欄位、`QuestProgress.TargetSnapshot`、`Goals` 的兩個索引。
- §6 API：加 `GET /goals/categories`、`POST /goals/preview`、`POST /goals`、`GET /goals`、`DELETE /goals/{id}`；`GET /me` 加 `needsOnboarding`；`GET /today` 任務加 `progression`；`PUT /quests/{id}` 加「漸進任務只能改屬性與難度，否則 400 `ProgressionQuestLocked`」；`POST /auth/register` 改為不建任務。
- §8 前端：加第五個畫面「引導」，三步說明。
- §11 驗收清單：加「新帳號登入進引導，建作息目標後今日出現第 1 階時間」「連續達標 3 天後第 4 天提早」「封存目標後任務消失」。

- [ ] **Step 2: ARCHITECTURE.md**

- 資料模型段加 `Goal` 與 `Quest` 漸進欄位的說明，指出「階段不存 DB，由達標天數算出」。
- 請求生命週期段加 `TodayContextLoader` 批次載入 `DoneDaysBeforeToday` 的說明，以及「判定與顯示一律用 `Progression.EffectiveTarget`」。
- Api 檔案表加 `Contracts/GoalDtos.cs`、`Services/GoalService.cs`、`Controllers/GoalsController.cs`；Domain 加 `Goals/` 目錄與 `Rules/TimeOfDay.cs`、`Rules/Progression.cs`。

- [ ] **Step 3: README.md**

「API 範例」區在註冊之後加：

```bash
# 建立目標（引導流程的最後一步；也可只帶 basicQuestIndexes 加入基本任務）
curl -s -X POST localhost:8080/api/v1/goals -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"goals":[{"category":"Routine","answers":{"currentBedtime":"01:00","targetBedtime":"00:00","currentWakeTime":"08:00","targetWakeTime":"07:00","lengthDays":30}}],"basicQuestIndexes":[2,4,6,8]}'
```

並把註冊範例的註解「自動建立 9 個預設任務」改成「不再自動建任務，見下方建立目標」。

- [ ] **Step 4: CLAUDE.md**

- 「關鍵不變式」加兩條：「**任務的判定與顯示一律用 `Progression.EffectiveTarget`／`RenderName`，不直接讀 `Quest.TargetValue`／`Name`。** 漸進任務的階段不存 DB，由 `TodayContext.DoneDaysBeforeToday` 算出，該字典由 `TodayContextLoader` 一次批次載入」；「**Goal 封存連帶封存其任務；漸進任務的目標欄位不可編輯**，要改就封存目標重建」。
- 「專案結構」的 Domain 一行加 `Goals/（類別定義與規劃器）`。
- 「測試慣例」的 `RegisterAsync` 說明加 `seedBasicQuests` 參數。
- 「目前狀態」更新測試數（跑 `dotnet test` 取實際數字）。

- [ ] **Step 5: 檢查與 Commit**

Run: `grep -rn '預設任務' docs/SPEC.md README.md CLAUDE.md docs/ARCHITECTURE.md`
Expected: 只剩描述「基本任務（原預設任務）」的地方，沒有「註冊後自動建立」的說法。

```bash
git add docs/SPEC.md docs/ARCHITECTURE.md README.md CLAUDE.md
git commit -m "docs: 補上引導式目標與漸進任務的規格、架構與使用說明" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```
