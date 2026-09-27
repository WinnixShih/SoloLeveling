namespace SoloLeveling.Domain.Entities;

/// <summary>
/// 使用者定義的每日習慣任務。刪除只做封存（<see cref="IsArchived"/>），不刪列。
/// </summary>
public class Quest
{
    /// <summary>主鍵。</summary>
    public Guid Id { get; set; }

    /// <summary>所屬使用者。</summary>
    public Guid UserId { get; set; }

    /// <summary>任務名稱，最長 60 字。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>完成時加點的屬性。</summary>
    public StatType StatType { get; set; }

    /// <summary>難度，決定 EXP 與屬性獎勵。</summary>
    public Difficulty Difficulty { get; set; }

    /// <summary>任務類型，決定完成判定方式。</summary>
    public QuestType QuestType { get; set; }

    /// <summary>目標值；Count 為需達到的累計量，Limit 為不可超過的上限；Check 類型忽略。</summary>
    public decimal? TargetValue { get; set; }

    /// <summary>前端 −/＋ 一次的增減量；Check 類型忽略。</summary>
    public decimal? Step { get; set; }

    /// <summary>單位顯示文字（例：杯、分鐘），最長 10 字。</summary>
    public string? Unit { get; set; }

    /// <summary>顯示順序，越小越前。</summary>
    public int SortOrder { get; set; }

    /// <summary>是否已封存；封存的任務不列入達標率分母，也不出現在任務清單。</summary>
    public bool IsArchived { get; set; }

    /// <summary>封存時間（UTC）；未封存為 null。</summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>建立時間（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
