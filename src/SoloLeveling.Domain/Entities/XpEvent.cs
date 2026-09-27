namespace SoloLeveling.Domain.Entities;

/// <summary>
/// EXP 流水；<see cref="Player.Xp"/> 的每一次變動都必須對應一筆事件。
/// </summary>
public class XpEvent
{
    /// <summary>主鍵。</summary>
    public Guid Id { get; set; }

    /// <summary>所屬使用者。</summary>
    public Guid UserId { get; set; }

    /// <summary>變動量；扣除為負值。</summary>
    public int Amount { get; set; }

    /// <summary>事件來源。</summary>
    public XpSource Source { get; set; }

    /// <summary>關聯物件 ID：任務類事件指向 Quest，達標／懲罰類事件指向 DailyLog。</summary>
    public Guid? RefId { get; set; }

    /// <summary>發生時間（UTC）。</summary>
    public DateTimeOffset OccurredAt { get; set; }
}
