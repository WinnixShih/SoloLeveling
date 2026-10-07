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
    /// <exception cref="DomainValidationException">類別未知、缺回答、多出類別沒有的回答鍵、天數不合法、回答格式或範圍不合法、目標不比現況好。</exception>
    public static IReadOnlyList<GoalQuestBlueprint> Plan(GoalCategory category, IReadOnlyDictionary<string, string> answers)
    {
        var definition = GoalCategories.Get(category);
        var knownKeys = definition.Questions.Select(q => q.Key).ToHashSet();
        var unknown = answers.Keys.FirstOrDefault(k => !knownKeys.Contains(k));
        if (unknown is not null)
        {
            throw new DomainValidationException("UnknownAnswer", $"此類別沒有「{unknown}」這個回答");
        }

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
