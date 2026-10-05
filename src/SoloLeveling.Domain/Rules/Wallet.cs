using SoloLeveling.Domain.Entities;

namespace SoloLeveling.Domain.Rules;

/// <summary>
/// 金幣異動的唯一入口：改 <see cref="Player.Coins"/> 並回傳對應的 <see cref="CoinEvent"/>，由呼叫端持久化。
/// </summary>
public static class Wallet
{
    /// <summary>
    /// 增減金幣。
    /// </summary>
    /// <param name="player">玩家。</param>
    /// <param name="amount">變動量；扣除為負。</param>
    /// <param name="source">來源。</param>
    /// <param name="refId">關聯物件 ID。</param>
    /// <param name="now">當下時間（UTC）。</param>
    /// <returns>這次的金幣事件。</returns>
    /// <exception cref="InvalidOperationException">扣除後餘額會小於 0（呼叫端應先以 <see cref="Spend"/> 檢查或自行夾住金額）。</exception>
    public static CoinEvent Change(Player player, int amount, CoinSource source, Guid? refId, DateTimeOffset now)
    {
        if (player.Coins + amount < 0)
        {
            throw new InvalidOperationException($"金幣不可為負：餘額 {player.Coins}，變動 {amount}");
        }

        player.Coins += amount;
        return new CoinEvent { Id = Guid.NewGuid(), UserId = player.UserId, Amount = amount, Source = source, RefId = refId, OccurredAt = now };
    }

    /// <summary>
    /// 花費金幣；餘額不足時不扣款。
    /// </summary>
    /// <param name="player">玩家。</param>
    /// <param name="price">價格（正值）。</param>
    /// <param name="source">來源。</param>
    /// <param name="refId">關聯物件 ID。</param>
    /// <param name="now">當下時間（UTC）。</param>
    /// <returns>這次的金幣事件（金額為負）。</returns>
    /// <exception cref="DomainValidationException">餘額不足（錯誤碼 <c>NotEnoughCoins</c>）。</exception>
    public static CoinEvent Spend(Player player, int price, CoinSource source, Guid? refId, DateTimeOffset now)
    {
        if (player.Coins < price)
        {
            throw new DomainValidationException("NotEnoughCoins", $"金幣不足：需要 {price}，目前 {player.Coins}");
        }

        return Change(player, -price, source, refId, now);
    }
}
