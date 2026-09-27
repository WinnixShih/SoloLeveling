using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoloLeveling.Api.Auth;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Services;

namespace SoloLeveling.Api.Controllers;

/// <summary>
/// 今日任務與進度。
/// </summary>
/// <param name="today">今日服務。</param>
[ApiController]
[Authorize]
[Route("api/v1/today")]
public class TodayController(TodayService today) : ControllerBase
{
    /// <summary>
    /// 今日任務與進度；呼叫前會先結算到昨日。
    /// </summary>
    /// <param name="ct">取消權杖。</param>
    /// <returns>今日。</returns>
    [HttpGet]
    [ProducesResponseType<TodayResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TodayResponse>> Get(CancellationToken ct)
    {
        return await today.GetTodayAsync(User.GetUserId(), ct);
    }

    /// <summary>
    /// 寫入某任務今日的進度值。Check 只接受 0／1，Count／Limit 不可為負；回傳更新後的今日。
    /// </summary>
    /// <param name="id">任務 ID。</param>
    /// <param name="request">新值。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>更新後的今日。</returns>
    [HttpPut("quests/{id:guid}/progress")]
    [ProducesResponseType<TodayResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TodayResponse>> SetProgress(Guid id, ProgressRequest request, CancellationToken ct)
    {
        return await today.SetProgressAsync(User.GetUserId(), id, request.Value, ct);
    }

    /// <summary>
    /// 寫入今日反思（最長 2000 字）。
    /// </summary>
    /// <param name="request">內容。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>204。</returns>
    [HttpPut("note")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetNote(NoteRequest request, CancellationToken ct)
    {
        await today.SetNoteAsync(User.GetUserId(), request.Note, ct);
        return NoContent();
    }
}
