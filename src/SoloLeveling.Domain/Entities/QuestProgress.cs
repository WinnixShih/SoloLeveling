namespace SoloLeveling.Domain.Entities;

/// <summary>
/// 某任務在某一天的進度；同一 <see cref="DailyLog"/> 下每個任務只有一筆。
/// </summary>
public class QuestProgress
{
    /// <summary>主鍵。</summary>
    public Guid Id { get; set; }

    /// <summary>所屬每日紀錄。</summary>
    public Guid DailyLogId { get; set; }

    /// <summary>所屬任務。</summary>
    public Guid QuestId { get; set; }

    /// <summary>進度值；意義依任務類型而定（Check 為 0/1、Count 為累計量、Limit 為實際量，null 表示未填）。</summary>
    public decimal? Value { get; set; }

    /// <summary>依任務類型判定的完成狀態快照。</summary>
    public bool IsDone { get; set; }

    /// <summary>完成時實際發出的 EXP；撤銷時依此扣回，撤銷後歸 0。</summary>
    public int XpGranted { get; set; }

    /// <summary>完成時實際發出的屬性點；撤銷時依此扣回，撤銷後歸 0。</summary>
    public int StatGranted { get; set; }

    /// <summary>建立時間（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
