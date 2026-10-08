namespace SoloLeveling.Domain.Entities;

/// <summary>
/// 一道傳送門挑戰。天數與可漏天數在開門時從 <see cref="Gates"/> 寫入，之後調整等級表不影響已開的門；
/// 每位使用者同時只有一筆 <see cref="Status"/> 為 <see cref="GateStatus.Active"/>。已漏天數不存欄位，每次由 DailyLogs 算出。
/// </summary>
public class Gate
{
    /// <summary>主鍵。</summary>
    public Guid Id { get; set; }

    /// <summary>所屬使用者。</summary>
    public Guid UserId { get; set; }

    /// <summary>門的等級。</summary>
    public GateRank Rank { get; set; }

    /// <summary>開門日（使用者時區），為第 1 天。</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>天數（開門時寫入的快照）。</summary>
    public int LengthDays { get; set; }

    /// <summary>最多可漏天數（開門時寫入的快照）；超過即失敗。</summary>
    public int AllowedMisses { get; set; }

    /// <summary>狀態；通關、失敗、放棄寫入後不再改變。</summary>
    public GateStatus Status { get; set; } = GateStatus.Active;

    /// <summary>通關、失敗或放棄的時間（UTC）；進行中為 null。</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>建立時間（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
