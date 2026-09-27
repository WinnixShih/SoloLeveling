using Microsoft.AspNetCore.Mvc;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Services;

namespace SoloLeveling.Api.Controllers;

/// <summary>
/// 註冊與登入；這兩支不需要 Bearer。
/// </summary>
/// <param name="accounts">帳號服務。</param>
[ApiController]
[Route("api/v1/auth")]
public class AuthController(AccountService accounts) : ControllerBase
{
    /// <summary>
    /// 註冊；成功後自動建立玩家、66 天週期與預設任務。
    /// </summary>
    /// <param name="request">註冊請求。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>201 與權杖。</returns>
    [HttpPost("register")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var response = await accounts.RegisterAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>
    /// 登入。
    /// </summary>
    /// <param name="request">登入請求。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>權杖。</returns>
    [HttpPost("login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        return await accounts.LoginAsync(request, ct);
    }
}
