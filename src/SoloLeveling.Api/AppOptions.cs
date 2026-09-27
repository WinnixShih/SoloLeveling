using System.ComponentModel.DataAnnotations;

namespace SoloLeveling.Api;

/// <summary>
/// 應用程式設定；屬性名稱即環境變數／設定鍵名稱，直接從設定根節點綁定。缺必要值時啟動失敗。
/// </summary>
public class AppOptions
{
    /// <summary>PostgreSQL 連線字串。</summary>
    [Required]
    public string DatabaseConnectionString { get; set; } = string.Empty;

    /// <summary>JWT 簽章金鑰（HS256），至少 32 bytes。</summary>
    [Required]
    [MinLength(32)]
    public string JwtSecret { get; set; } = string.Empty;
}
