namespace SoloLeveling.Domain;

/// <summary>
/// 一個等級的傳送門定義。
/// </summary>
/// <param name="Rank">等級。</param>
/// <param name="LengthDays">天數；開門當天為第 1 天。</param>
/// <param name="AllowedMisses">最多可漏天數；超過即失敗。</param>
/// <param name="Chests">每次通關發的寶箱等級，依序。</param>
/// <param name="AchievementKey">首次通關解鎖的成就鍵（見 <see cref="Achievements"/>）。</param>
/// <param name="RequiredRank">需先通關至少一次的等級；E 級為 null。</param>
public sealed record GateDefinition(
    GateRank Rank,
    int LengthDays,
    int AllowedMisses,
    IReadOnlyList<Rarity> Chests,
    string AchievementKey,
    GateRank? RequiredRank);

/// <summary>
/// 傳送門等級表；順序即顯示順序，也是解鎖順序。S 級寶箱只出現在 A、S 級門。
/// </summary>
public static class Gates
{
    /// <summary>六級門。</summary>
    public static IReadOnlyList<GateDefinition> All { get; } =
    [
        new(GateRank.E, 5, 1, [Rarity.E], "gate-e", null),
        new(GateRank.D, 10, 2, [Rarity.C], "gate-d", GateRank.E),
        new(GateRank.C, 20, 3, [Rarity.A], "gate-c", GateRank.D),
        new(GateRank.B, 35, 5, [Rarity.A, Rarity.A], "gate-b", GateRank.C),
        new(GateRank.A, 60, 8, [Rarity.S, Rarity.A], "gate-a", GateRank.B),
        new(GateRank.S, 100, 10, [Rarity.S, Rarity.S, Rarity.S], "gate-s", GateRank.A),
    ];

    private static readonly Dictionary<GateRank, GateDefinition> Index = All.ToDictionary(g => g.Rank);

    /// <summary>
    /// 依等級取定義。
    /// </summary>
    /// <param name="rank">等級。</param>
    /// <returns>定義。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義的等級（呼叫端應先驗證輸入）。</exception>
    public static GateDefinition Of(GateRank rank)
    {
        if (!Index.TryGetValue(rank, out var definition))
        {
            throw new ArgumentOutOfRangeException(nameof(rank), rank, "未定義的傳送門等級");
        }

        return definition;
    }

    /// <summary>
    /// 是否已解鎖：E 級永遠可開，其餘需低一級通關至少一次。
    /// </summary>
    /// <param name="rank">要開的等級。</param>
    /// <param name="clearCounts">各等級的通關次數；沒有通關的等級可不在字典內。</param>
    /// <returns>已解鎖為 true。</returns>
    public static bool IsUnlocked(GateRank rank, IReadOnlyDictionary<GateRank, int> clearCounts)
    {
        return Of(rank).RequiredRank is not { } required || clearCounts.GetValueOrDefault(required) > 0;
    }
}
