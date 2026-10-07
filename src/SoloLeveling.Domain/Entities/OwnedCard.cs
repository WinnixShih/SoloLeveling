namespace SoloLeveling.Domain.Entities;

/// <summary>
/// 使用者擁有的卡片；同一使用者同一張卡只有一列，重複抽到只加張數。
/// </summary>
public class OwnedCard
{
    /// <summary>所屬使用者（複合主鍵之一）。</summary>
    public Guid UserId { get; set; }

    /// <summary>卡片 ID（複合主鍵之一），對應 <c>Cards.All</c>。</summary>
    public string CardId { get; set; } = string.Empty;

    /// <summary>持有張數，至少 1。</summary>
    public int Count { get; set; }

    /// <summary>首次取得時間（UTC）。</summary>
    public DateTimeOffset FirstAcquiredAt { get; set; }
}
