namespace SoloLeveling.Domain.Entities;

/// <summary>
/// 需要通知使用者的獎勵事件（目前只有保險卡生效）；可能由排程結算產生，下一次請求回報後寫入 <see cref="AnnouncedAt"/>。
/// </summary>
public class RewardEvent
{
    /// <summary>主鍵。</summary>
    public Guid Id { get; set; }

    /// <summary>所屬使用者。</summary>
    public Guid UserId { get; set; }

    /// <summary>事件種類。</summary>
    public RewardEventKind Kind { get; set; }

    /// <summary>事件對應的日期（使用者時區；保險卡為被保護的那一天）。</summary>
    public DateOnly Date { get; set; }

    /// <summary>發生時間（UTC）。</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>已回報給前端的時間（UTC）；null 表示尚未回報。</summary>
    public DateTimeOffset? AnnouncedAt { get; set; }
}
