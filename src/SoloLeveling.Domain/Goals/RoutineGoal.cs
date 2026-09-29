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
