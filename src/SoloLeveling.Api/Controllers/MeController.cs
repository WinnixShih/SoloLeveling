using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoloLeveling.Api.Auth;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Services;

namespace SoloLeveling.Api.Controllers;

/// <summary>
/// 玩家總覽與設定。
/// </summary>
/// <param name="players">玩家服務。</param>
[ApiController]
[Authorize]
[Route("api/v1/me")]
public class MeController(PlayerService players) : ControllerBase
{
    /// <summary>
    /// 玩家總覽；呼叫前會先結算到昨日。
    /// </summary>
    /// <param name="ct">取消權杖。</param>
    /// <returns>總覽。</returns>
    [HttpGet]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeResponse>> Get(CancellationToken ct)
    {
        return await players.GetMeAsync(User.GetUserId(), ct);
    }

    /// <summary>
    /// 更新設定；hardMode 變更會立即重算今日達標與獎勵。
    /// </summary>
    /// <param name="request">要更新的欄位，省略者不動。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>更新後的總覽。</returns>
    [HttpPatch]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MeResponse>> Patch(PatchMeRequest request, CancellationToken ct)
    {
        return await players.PatchMeAsync(User.GetUserId(), request, ct);
    }
}
