namespace SoloLeveling.Domain.Entities;

/// <summary>
/// 寶箱；建立時未開啟，使用者手動開啟後寫入掉落卡片與金幣。
/// </summary>
public class RewardChest
{
    /// <summary>主鍵。</summary>
    public Guid Id { get; set; }

    /// <summary>所屬使用者。</summary>
    public Guid UserId { get; set; }

    /// <summary>寶箱等級，決定抽卡池與金幣。</summary>
    public Rarity Rarity { get; set; }

    /// <summary>取得來源。</summary>
    public ChestSource Source { get; set; }

    /// <summary>取得時間（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>開啟時間（UTC）；未開啟為 null。</summary>
    public DateTimeOffset? OpenedAt { get; set; }

    /// <summary>掉落的卡片 ID；未開啟為 null。</summary>
    public string? DroppedCardId { get; set; }

    /// <summary>開啟時得到的金幣（開箱＋重複卡）；未開啟為 0。</summary>
    public int Coins { get; set; }
}
