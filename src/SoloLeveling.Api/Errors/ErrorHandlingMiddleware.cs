using SoloLeveling.Api.Contracts;
using SoloLeveling.Domain;

namespace SoloLeveling.Api.Errors;

/// <summary>
/// 把 <see cref="ApiErrorException"/> 與 <see cref="DomainValidationException"/> 轉成統一的 <c>{ error: { code, message } }</c> 回應；其他例外交給框架的 500 處理。
/// </summary>
/// <param name="next">下一個中介軟體。</param>
public class ErrorHandlingMiddleware(RequestDelegate next)
{
    /// <summary>
    /// 執行請求並攔截已知的錯誤例外。
    /// </summary>
    /// <param name="context">HTTP 內容。</param>
    /// <returns>非同步作業。</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiErrorException ex)
        {
            await WriteAsync(context, ex.StatusCode, ex.Code, ex.Message);
        }
        catch (DomainValidationException ex)
        {
            await WriteAsync(context, StatusCodes.Status400BadRequest, ex.Code, ex.Message);
        }
    }

    private static Task WriteAsync(HttpContext context, int statusCode, string code, string message)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new ErrorResponse(new ErrorBody(code, message)));
    }
}
