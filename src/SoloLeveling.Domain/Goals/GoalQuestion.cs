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
