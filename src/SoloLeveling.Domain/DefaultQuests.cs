namespace SoloLeveling.Domain;

/// <summary>
/// 預設任務樣板。
/// </summary>
/// <param name="Name">任務名稱。</param>
/// <param name="StatType">屬性。</param>
/// <param name="Difficulty">難度。</param>
/// <param name="QuestType">任務類型。</param>
/// <param name="TargetValue">目標值；Check 類型為 null。</param>
/// <param name="Step">增減量；Check 類型為 null。</param>
/// <param name="Unit">單位；Check 類型為 null。</param>
public record QuestTemplate(string Name, StatType StatType, Difficulty Difficulty, QuestType QuestType, decimal? TargetValue, decimal? Step, string? Unit);

/// <summary>
/// 新使用者註冊後自動建立的 9 個任務（規格 4.10），順序即 SortOrder。
/// </summary>
public static class DefaultQuests
{
    /// <summary>預設任務清單。</summary>
    public static IReadOnlyList<QuestTemplate> All { get; } =
    [
        new("23:30 前上床睡覺", StatType.Vitality, Difficulty.Normal, QuestType.Check, null, null, null),
        new("7:30 前起床", StatType.Vitality, Difficulty.Hard, QuestType.Check, null, null, null),
        new("喝水", StatType.Vitality, Difficulty.Easy, QuestType.Count, 8, 1, "杯"),
        new("閱讀", StatType.Intelligence, Difficulty.Normal, QuestType.Count, 20, 5, "分鐘"),
        new("技術學習 30 分鐘", StatType.Intelligence, Difficulty.Normal, QuestType.Check, null, null, null),
        new("手機螢幕時間", StatType.Willpower, Difficulty.Hard, QuestType.Limit, 3, 0.5m, "小時"),
        new("睡前 1 小時不滑手機", StatType.Willpower, Difficulty.Hard, QuestType.Check, null, null, null),
        new("運動 30 分鐘", StatType.Strength, Difficulty.Normal, QuestType.Check, null, null, null),
        new("寫下今天的三件好事", StatType.Spirit, Difficulty.Easy, QuestType.Check, null, null, null),
    ];
}
