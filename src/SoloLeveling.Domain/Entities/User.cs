namespace SoloLeveling.Domain.Entities;

/// <summary>
/// 使用者帳號；一個 User 對應一個 <see cref="Player"/>。
/// </summary>
public class User
{
    /// <summary>主鍵。</summary>
    public Guid Id { get; set; }

    /// <summary>登入用 Email，不分大小寫唯一。</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>PBKDF2 密碼雜湊（含 salt 與參數的自描述字串）。</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>顯示名稱，最長 40 字。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>IANA 時區 ID（例：<c>Asia/Taipei</c>），用來換算使用者的「今日」。</summary>
    public string TimeZoneId { get; set; } = "Asia/Taipei";

    /// <summary>建立時間（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
