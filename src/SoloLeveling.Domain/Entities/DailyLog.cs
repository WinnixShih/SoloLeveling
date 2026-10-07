namespace SoloLeveling.Domain.Entities;

/// <summary>
/// 某使用者某一天的快照；結算（<see cref="IsSettled"/>）後不可修改。
/// </summary>
public class DailyLog
{
    /// <summary>主鍵。</summary>
    public Guid Id { get; set; }

    /// <summary>所屬使用者。</summary>
    public Guid UserId { get; set; }

    /// <summary>日期（使用者時區）；同一使用者同一天只有一筆。</summary>
    public DateOnly Date { get; set; }

    /// <summary>達標率 = 完成數 / 未封存任務數，範圍 0–1；無任務時為 0。</summary>
    public decimal CompletionRatio { get; set; }

    /// <summary>是否達標（達標率 &gt;= 當前模式門檻）。</summary>
    public bool IsCleared { get; set; }

    /// <summary>是否已發放當日達標獎勵（+30 EXP）。</summary>
    public bool BonusGranted { get; set; }

    /// <summary>是否已發放當日達標金幣（+10）；跟著 <see cref="BonusGranted"/> 發放與收回。</summary>
    public bool ClearCoinsGranted { get; set; }

    /// <summary>當日反思筆記，最長 2000 字。</summary>
    public string? Note { get; set; }

    /// <summary>是否已結算；結算後 Streak 與懲罰已套用，不再變動。</summary>
    public bool IsSettled { get; set; }

    /// <summary>結算時間（UTC）；未結算為 null。</summary>
    public DateTimeOffset? SettledAt { get; set; }

    /// <summary>建立時間（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>當日各任務的進度。</summary>
    public ICollection<QuestProgress> Progresses { get; set; } = new List<QuestProgress>();
}
