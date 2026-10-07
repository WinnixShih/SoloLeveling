namespace SoloLeveling.Domain.Entities;

/// <summary>
/// 已購買的主題；預設主題 azure 不需要列。
/// </summary>
public class OwnedTheme
{
    /// <summary>所屬使用者（複合主鍵之一）。</summary>
    public Guid UserId { get; set; }

    /// <summary>主題鍵（複合主鍵之一）。</summary>
    public string ThemeKey { get; set; } = string.Empty;
}
