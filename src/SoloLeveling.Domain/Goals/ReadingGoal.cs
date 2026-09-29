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
