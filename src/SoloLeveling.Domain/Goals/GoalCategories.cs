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
