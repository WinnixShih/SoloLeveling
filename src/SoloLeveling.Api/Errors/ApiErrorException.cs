namespace SoloLeveling.Api.Errors;

/// <summary>
/// 帶 HTTP 狀態碼與錯誤代碼的例外；由 <see cref="ErrorHandlingMiddleware"/> 轉成統一的錯誤回應。
/// </summary>
/// <param name="statusCode">HTTP 狀態碼。</param>
/// <param name="code">機器可讀的錯誤代碼。</param>
/// <param name="message">給人看的訊息。</param>
public class ApiErrorException(int statusCode, string code, string message) : Exception(message)
{
    /// <summary>HTTP 狀態碼。</summary>
    public int StatusCode { get; } = statusCode;

    /// <summary>機器可讀的錯誤代碼。</summary>
    public string Code { get; } = code;

    /// <summary>400。</summary>
    public static ApiErrorException BadRequest(string code, string message)
    {
        return new ApiErrorException(StatusCodes.Status400BadRequest, code, message);
    }

    /// <summary>401。</summary>
    public static ApiErrorException Unauthorized(string code, string message)
    {
        return new ApiErrorException(StatusCodes.Status401Unauthorized, code, message);
    }

    /// <summary>404。</summary>
    public static ApiErrorException NotFound(string code, string message)
    {
        return new ApiErrorException(StatusCodes.Status404NotFound, code, message);
    }

    /// <summary>409。</summary>
    public static ApiErrorException Conflict(string code, string message)
    {
        return new ApiErrorException(StatusCodes.Status409Conflict, code, message);
    }
}
