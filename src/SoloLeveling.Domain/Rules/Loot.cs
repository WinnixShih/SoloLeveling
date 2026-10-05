namespace SoloLeveling.Domain.Rules;

/// <summary>
/// 開箱結果。
/// </summary>
/// <param name="CardId">抽到的卡片 ID。</param>
/// <param name="IsDuplicate">開箱前是否已擁有這張卡。</param>
/// <param name="BaseCoins">開箱固定金幣。</param>
/// <param name="DuplicateCoins">重複卡轉換的金幣；新卡為 0。</param>
public sealed record LootResult(string CardId, bool IsDuplicate, int BaseCoins, int DuplicateCoins)
{
    /// <summary>本次開箱共得金幣。</summary>
    public int Coins => BaseCoins + DuplicateCoins;
}

/// <summary>
/// 開箱規則：從寶箱等級的全部卡片中均勻抽一張（含已擁有），另附固定金幣；重複卡轉成金幣。
/// </summary>
public static class Loot
{
    /// <summary>
    /// 開箱固定金幣。
    /// </summary>
    /// <param name="rarity">寶箱等級。</param>
    /// <returns>E 20、C 50、A 100、S 300。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未知的稀有度。</exception>
    public static int OpenCoinsOf(Rarity rarity)
    {
        return rarity switch
        {
            Rarity.E => 20,
            Rarity.C => 50,
            Rarity.A => 100,
            Rarity.S => 300,
            _ => throw new ArgumentOutOfRangeException(nameof(rarity), rarity, "未知的稀有度"),
        };
    }

    /// <summary>
    /// 重複卡轉換的金幣。
    /// </summary>
    /// <param name="rarity">卡片稀有度。</param>
    /// <returns>E 30、C 80、A 200、S 500。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未知的稀有度。</exception>
    public static int DuplicateCoinsOf(Rarity rarity)
    {
        return rarity switch
        {
            Rarity.E => 30,
            Rarity.C => 80,
            Rarity.A => 200,
            Rarity.S => 500,
            _ => throw new ArgumentOutOfRangeException(nameof(rarity), rarity, "未知的稀有度"),
        };
    }

    /// <summary>
    /// 開一個寶箱；只計算結果，不修改任何實體。
    /// </summary>
    /// <param name="rarity">寶箱等級。</param>
    /// <param name="ownedCounts">開箱前已擁有的卡片張數（卡片 ID → 張數）。</param>
    /// <param name="rng">亂數來源（注入，測試可固定）。</param>
    /// <returns>抽到的卡片與金幣。</returns>
    public static LootResult Open(Rarity rarity, IReadOnlyDictionary<string, int> ownedCounts, Random rng)
    {
        var pool = Cards.OfRarity(rarity);
        var card = pool[rng.Next(pool.Count)];
        var isDuplicate = ownedCounts.TryGetValue(card.Id, out var count) && count > 0;
        return new LootResult(card.Id, isDuplicate, OpenCoinsOf(rarity), isDuplicate ? DuplicateCoinsOf(rarity) : 0);
    }
}
