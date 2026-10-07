namespace SoloLeveling.Domain.Entities;

/// <summary>
/// 66 天計畫週期；每位使用者同時只有一筆 <see cref="IsActive"/> 為 true。
/// </summary>
public class Program
{
    /// <summary>計畫長度的固定值（天）。</summary>
    public const int DefaultLengthDays = 66;

    /// <summary>主鍵。</summary>
    public Guid Id { get; set; }

    /// <summary>所屬使用者。</summary>
    public Guid UserId { get; set; }

    /// <summary>週期起始日（使用者時區）。</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>第幾個週期，從 1 起算；開新週期時 +1。</summary>
    public int Cycle { get; set; } = 1;

    /// <summary>週期長度（天）。</summary>
    public int LengthDays { get; set; } = DefaultLengthDays;

    /// <summary>是否為目前進行中的週期。</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>完成時間（UTC）：開新週期時舊週期已滿 <see cref="LengthDays"/> 天才寫入；未完成為 null。</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>建立時間（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
