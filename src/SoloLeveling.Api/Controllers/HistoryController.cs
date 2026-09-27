using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoloLeveling.Api.Auth;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Services;

namespace SoloLeveling.Api.Controllers;

/// <summary>
/// 每日紀錄與 EXP 流水。
/// </summary>
/// <param name="history">查詢服務。</param>
[ApiController]
[Authorize]
[Route("api/v1")]
public class HistoryController(HistoryService history) : ControllerBase
{
    /// <summary>
    /// 區間內每一天的紀錄（含今日即時值），最多 100 天，由舊到新。
    /// </summary>
    /// <param name="from">起日（YYYY-MM-DD，含）。</param>
    /// <param name="to">迄日（YYYY-MM-DD，含）。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>紀錄清單。</returns>
    [HttpGet("history")]
    [ProducesResponseType<List<HistoryDayDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<HistoryDayDto>>> GetHistory([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        return await history.GetHistoryAsync(User.GetUserId(), from, to, ct);
    }

    /// <summary>
    /// EXP 流水，新到舊。
    /// </summary>
    /// <param name="limit">筆數，預設 50，最多 200。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>事件清單。</returns>
    [HttpGet("xp-events")]
    [ProducesResponseType<List<XpEventDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<XpEventDto>>> GetXpEvents([FromQuery] int limit = HistoryService.DefaultEventLimit, CancellationToken ct = default)
    {
        return await history.GetXpEventsAsync(User.GetUserId(), limit, ct);
    }
}
