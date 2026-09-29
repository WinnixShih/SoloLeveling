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
