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

    /// <summary>所屬的引導式目標；null 表示一般任務，以下漸進欄位全為 null。</summary>
    public Guid? GoalId { get; set; }

    /// <summary>漸進目標值的種類。</summary>
    public ProgressionValueKind? ValueKind { get; set; }

    /// <summary>漸進起點（使用者的現況）。時間類為距中午的分鐘數。</summary>
    public decimal? StartValue { get; set; }

    /// <summary>漸進終點（使用者的目標）。</summary>
    public decimal? EndValue { get; set; }

    /// <summary>每升一階的變化量，帶正負號。</summary>
    public decimal? StepValue { get; set; }

    /// <summary>總階數；達到最後一階後目標固定為 <see cref="EndValue"/>。</summary>
    public int? StageCount { get; set; }

    /// <summary>升一階需要累積的達標天數（不需連續）。</summary>
    public int? DaysPerStep { get; set; }
}
