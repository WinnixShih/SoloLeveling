namespace SoloLeveling.Domain.Entities;

/// <summary>
/// 金幣流水；<see cref="Player.Coins"/> 的每一次變動都必須對應一筆事件。
/// </summary>
public class CoinEvent
{
    /// <summary>主鍵。</summary>
    public Guid Id { get; set; }

    /// <summary>DB 產生的遞增序號；同一請求內多筆事件的 <see cref="OccurredAt"/> 相同，靠此欄位穩定排序。</summary>
    public long Seq { get; set; }

    /// <summary>所屬使用者。</summary>
    public Guid UserId { get; set; }

    /// <summary>變動量；扣除為負值。</summary>
    public int Amount { get; set; }

    /// <summary>事件來源。</summary>
    public CoinSource Source { get; set; }

    /// <summary>關聯物件 ID：達標類指向 DailyLog，開箱類指向寶箱；成就與商店保險卡、主題為 null。</summary>
    public Guid? RefId { get; set; }

    /// <summary>發生時間（UTC）。</summary>
    public DateTimeOffset OccurredAt { get; set; }
}
