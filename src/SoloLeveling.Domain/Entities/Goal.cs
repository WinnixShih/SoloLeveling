namespace SoloLeveling.Domain.Entities;

/// <summary>
/// 引導式目標：使用者對某類別的回答與期限，產生一到多個漸進任務。刪除只做封存。
/// </summary>
public class Goal
{
    /// <summary>主鍵。</summary>
    public Guid Id { get; set; }

    /// <summary>所屬使用者。</summary>
    public Guid UserId { get; set; }

    /// <summary>類別。</summary>
    public GoalCategory Category { get; set; }

    /// <summary>原始回答的 JSON 物件字串（鍵為問題 key，值為字串）；保留給日後重新規劃使用。</summary>
    public string Answers { get; set; } = "{}";

    /// <summary>目標天數（7–90）。</summary>
    public int LengthDays { get; set; }

    /// <summary>建立當天（使用者時區）。</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>是否已封存；封存時連帶封存其任務。</summary>
    public bool IsArchived { get; set; }

    /// <summary>封存時間（UTC）。</summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>完成時間（UTC）：所有未封存任務都在最後一階達標過時寫入，之後不再清除；用來保證 A 級寶箱只發一次。</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>建立時間（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
