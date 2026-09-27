namespace SoloLeveling.Domain;

/// <summary>
/// 領域規則驗證失敗（輸入不合法）；API 層對應為 400。
/// </summary>
/// <param name="code">機器可讀的錯誤代碼（例：<c>InvalidValue</c>）。</param>
/// <param name="message">給人看的錯誤訊息。</param>
public class DomainValidationException(string code, string message) : Exception(message)
{
    /// <summary>機器可讀的錯誤代碼。</summary>
    public string Code { get; } = code;
}
