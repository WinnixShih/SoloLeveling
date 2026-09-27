namespace SoloLeveling.Domain.Rules;

/// <summary>
/// 完成任務的獎勵。
/// </summary>
/// <param name="Xp">EXP 獎勵。</param>
/// <param name="Stat">屬性點獎勵。</param>
public record QuestReward(int Xp, int Stat);

/// <summary>
/// 任務完成判定、獎勵常數與達標率規則。
/// </summary>
public static class CompletionRules
{
    /// <summary>當日達標獎勵 EXP。</summary>
    public const int DailyBonusXp = 30;

    /// <summary>困難模式未達標懲罰比例（乘上 XpNeeded）。</summary>
    public const decimal PenaltyRate = 0.15m;

    /// <summary>一般模式達標門檻。</summary>
    public const decimal NormalThreshold = 0.70m;

    /// <summary>困難模式達標門檻。</summary>
    public const decimal HardThreshold = 1.00m;

    /// <summary>
    /// 依任務類型判定進度值是否算完成。
    /// </summary>
    /// <param name="questType">任務類型。</param>
    /// <param name="value">進度值；null 表示未填。</param>
    /// <param name="targetValue">目標值；Check 類型忽略。</param>
    /// <returns>是否完成。</returns>
    public static bool IsDone(QuestType questType, decimal? value, decimal? targetValue)
    {
        return questType switch
        {
            QuestType.Check => value >= 1,
            QuestType.Count => value >= targetValue,
            QuestType.Limit => value != null && value <= targetValue,
            _ => false,
        };
    }

    /// <summary>
    /// 依難度取得完成獎勵。
    /// </summary>
    /// <param name="difficulty">難度。</param>
    /// <returns>EXP 與屬性獎勵。</returns>
    public static QuestReward RewardOf(Difficulty difficulty)
    {
        return difficulty switch
        {
            Difficulty.Easy => new QuestReward(10, 1),
            Difficulty.Normal => new QuestReward(20, 1),
            Difficulty.Hard => new QuestReward(35, 2),
            _ => throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "未知的難度"),
        };
    }

    /// <summary>
    /// 依模式取得達標門檻。
    /// </summary>
    /// <param name="hardMode">是否困難模式。</param>
    /// <returns>達標率門檻（0–1）。</returns>
    public static decimal ThresholdOf(bool hardMode)
    {
        return hardMode ? HardThreshold : NormalThreshold;
    }

    /// <summary>
    /// 計算達標率；無任務時為 0，結果四捨五入到小數 4 位（對應 DB 的 decimal(5,4)）。
    /// </summary>
    /// <param name="doneCount">完成數。</param>
    /// <param name="activeQuestCount">未封存任務數。</param>
    /// <returns>達標率（0–1）。</returns>
    public static decimal CompletionRatio(int doneCount, int activeQuestCount)
    {
        if (activeQuestCount == 0)
        {
            return 0m;
        }

        return Math.Round((decimal)doneCount / activeQuestCount, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 困難模式未達標的懲罰量：<c>round(XpNeeded(level) * 0.15)</c>。
    /// </summary>
    /// <param name="level">玩家目前等級。</param>
    /// <returns>要扣的 EXP。</returns>
    public static int PenaltyOf(int level)
    {
        return (int)Math.Round(Leveling.XpNeeded(level) * PenaltyRate, MidpointRounding.AwayFromZero);
    }
}
