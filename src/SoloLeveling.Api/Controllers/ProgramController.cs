using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoloLeveling.Api.Auth;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Services;

namespace SoloLeveling.Api.Controllers;

/// <summary>
/// 66 天計畫週期。
/// </summary>
/// <param name="programs">週期服務。</param>
[ApiController]
[Authorize]
[Route("api/v1/program")]
public class ProgramController(ProgramService programs) : ControllerBase
{
    /// <summary>
    /// 從今日開新的 66 天週期（Cycle + 1）；不重置玩家等級與屬性。
    /// </summary>
    /// <param name="ct">取消權杖。</param>
    /// <returns>新週期。</returns>
    [HttpPost("restart")]
    [ProducesResponseType<ProgramDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProgramDto>> Restart(CancellationToken ct)
    {
        return await programs.RestartAsync(User.GetUserId(), ct);
    }
}
