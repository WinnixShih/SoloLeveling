namespace SoloLeveling.Domain.Entities;

/// <summary>
/// 已解鎖的成就；主鍵 (UserId, Key) 擋重複解鎖。
/// </summary>
public class Achievement
{
    /// <summary>所屬使用者（複合主鍵之一）。</summary>
    public Guid UserId { get; set; }

    /// <summary>成就鍵（複合主鍵之一），對應 <c>Achievements.All</c>。</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>解鎖時間（UTC）。</summary>
    public DateTimeOffset UnlockedAt { get; set; }
}
