using SoloLeveling.Domain.Entities;

namespace SoloLeveling.Domain.Rules;

/// <summary>
/// 目標完成判定：目標的每個未封存任務都在最後一階至少達標一天。
/// </summary>
public static class GoalCompletion
{
    /// <summary>
    /// 任務要累積幾天達標（含今天）才算在最後一階達標過：<c>(StageCount − 1) × DaysPerStep + 1</c>。
    /// </summary>
    /// <param name="quest">漸進任務。</param>
    /// <returns>需要的達標天數。</returns>
    public static int DoneDaysToFinish(Quest quest)
    {
        var stageCount = quest.StageCount ?? 1;
        var daysPerStep = quest.DaysPerStep ?? Progression.DefaultDaysPerStep;
        return ((stageCount - 1) * daysPerStep) + 1;
    }

    /// <summary>
    /// 目標是否完成。
    /// </summary>
    /// <param name="activeQuests">目標底下未封存的任務；空集合（任務都被單獨封存）不算完成。</param>
    /// <param name="doneDaysIncludingToday">取某任務的達標天數（今天之前＋今天是否完成）。</param>
    /// <returns>是否完成。</returns>
    public static bool IsFinished(IReadOnlyCollection<Quest> activeQuests, Func<Quest, int> doneDaysIncludingToday)
    {
        return activeQuests.Count > 0 && activeQuests.All(q => doneDaysIncludingToday(q) >= DoneDaysToFinish(q));
    }
}
