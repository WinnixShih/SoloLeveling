namespace SoloLeveling.Domain.Rules;

/// <summary>
/// 由漸進任務推導角色（不新增欄位），以及分類成就用的連續天數算法。一般任務沒有角色。
/// </summary>
public static class QuestRoles
{
    /// <summary>計算分類連續天數時往前看的天數；成就門檻最多 30 天，查 [today − 30, today] 即足夠。</summary>
    public const int StreakWindowDays = 30;

    /// <summary>
    /// 由目標類別與值種類推導角色。
    /// </summary>
    /// <param name="category">任務所屬目標的類別。</param>
    /// <param name="valueKind">任務的值種類。</param>
    /// <returns>角色；無法對應時為 null。</returns>
    public static QuestRole? Of(GoalCategory category, ProgressionValueKind? valueKind)
    {
        return category switch
        {
            GoalCategory.Routine when valueKind == ProgressionValueKind.TimeOfDay => QuestRole.Bedtime,
            GoalCategory.Routine when valueKind == ProgressionValueKind.TimeOfDayEvening => QuestRole.WakeUp,
            GoalCategory.Exercise => QuestRole.Exercise,
            GoalCategory.Reading => QuestRole.Reading,
            GoalCategory.ScreenTime => QuestRole.ScreenTime,
            _ => null,
        };
    }

    /// <summary>
    /// 連續天數：到昨天為止連續幾天完成，今天已完成則再 +1。
    /// </summary>
    /// <param name="doneDates">該任務完成的日期（至少涵蓋 [today − <see cref="StreakWindowDays"/>, today]）。</param>
    /// <param name="today">使用者時區的今日。</param>
    /// <returns>連續天數。</returns>
    public static int StreakOf(IReadOnlySet<DateOnly> doneDates, DateOnly today)
    {
        var streak = doneDates.Contains(today) ? 1 : 0;
        for (var date = today.AddDays(-1); doneDates.Contains(date); date = date.AddDays(-1))
        {
            streak += 1;
        }

        return streak;
    }

    /// <summary>
    /// 該角色是否累計分鐘（運動、閱讀的進度值即分鐘數）。
    /// </summary>
    /// <param name="role">角色。</param>
    /// <returns>是否累計分鐘。</returns>
    public static bool CountsMinutes(QuestRole role)
    {
        return role is QuestRole.Exercise or QuestRole.Reading;
    }
}
