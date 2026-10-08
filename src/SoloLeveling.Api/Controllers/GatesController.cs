using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoloLeveling.Api.Auth;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Services;

namespace SoloLeveling.Api.Controllers;

/// <summary>
/// 傳送門挑戰。
/// </summary>
/// <param name="gates">傳送門服務。</param>
[ApiController]
[Authorize]
[Route("api/v1/gates")]
public class GatesController(GateService gates) : ControllerBase
{
    /// <summary>
    /// 六級門（天數、可漏天數、獎勵、是否解鎖、通關次數）與進行中的門；呼叫前會先結算並補判定。
    /// </summary>
    /// <param name="ct">取消權杖。</param>
    /// <returns>等級表與進行中的門。</returns>
    [HttpGet]
    [ProducesResponseType<GatesResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GatesResponse>> List(CancellationToken ct)
    {
        return await gates.ListAsync(User.GetUserId(), ct);
    }

    /// <summary>
    /// 開一道已解鎖的門，今日為第 1 天。
    /// </summary>
    /// <param name="request">等級。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>進行中的門（含本次獎勵）。</returns>
    [HttpPost]
    [ProducesResponseType<ActiveGateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ActiveGateDto>> Open(OpenGateRequest request, CancellationToken ct)
    {
        return await gates.OpenAsync(User.GetUserId(), request.Rank, ct);
    }

    /// <summary>
    /// 放棄進行中的門。
    /// </summary>
    /// <param name="ct">取消權杖。</param>
    /// <returns>本次獎勵。</returns>
    [HttpPost("active/abandon")]
    [ProducesResponseType<RewardsEnvelope>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RewardsEnvelope>> Abandon(CancellationToken ct)
    {
        return await gates.AbandonAsync(User.GetUserId(), ct);
    }
}
