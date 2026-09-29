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
