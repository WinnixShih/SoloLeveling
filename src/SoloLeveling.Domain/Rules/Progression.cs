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
