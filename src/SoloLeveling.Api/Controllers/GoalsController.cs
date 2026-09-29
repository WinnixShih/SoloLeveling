using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoloLeveling.Api.Auth;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Services;

namespace SoloLeveling.Api.Controllers;

/// <summary>
/// 引導式目標。
/// </summary>
/// <param name="goals">目標服務。</param>
[ApiController]
[Authorize]
[Route("api/v1/goals")]
public class GoalsController(GoalService goals) : ControllerBase
{
    /// <summary>
    /// 類別定義（問題清單）與基本任務清單，供引導流程畫表單。
    /// </summary>
    /// <returns>定義。</returns>
    [HttpGet("categories")]
    [ProducesResponseType<CategoriesResponse>(StatusCodes.Status200OK)]
    public ActionResult<CategoriesResponse> Categories()
    {
        return GoalService.Categories();
    }

    /// <summary>
    /// 預覽會產生的任務與階段摘要，不寫入。
    /// </summary>
    /// <param name="request">目標與回答。</param>
    /// <returns>預覽。</returns>
    [HttpPost("preview")]
    [ProducesResponseType<PreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public ActionResult<PreviewResponse> Preview(CreateGoalsRequest request)
    {
        return GoalService.Preview(request);
    }

    /// <summary>
    /// 建立目標、其漸進任務與勾選的基本任務。
    /// </summary>
    /// <param name="request">目標與回答。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>201 與進行中的目標。</returns>
    [HttpPost]
    [ProducesResponseType<GoalsResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GoalsResponse>> Create(CreateGoalsRequest request, CancellationToken ct)
    {
        var created = await goals.CreateAsync(User.GetUserId(), request, ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>
    /// 進行中的目標與各任務的當前階段。
    /// </summary>
    /// <param name="ct">取消權杖。</param>
    /// <returns>目標清單。</returns>
    [HttpGet]
    [ProducesResponseType<GoalsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GoalsResponse>> List(CancellationToken ct)
    {
        return await goals.ListAsync(User.GetUserId(), ct);
    }

    /// <summary>
    /// 封存目標與其任務。
    /// </summary>
    /// <param name="id">目標 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>204。</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await goals.ArchiveAsync(User.GetUserId(), id, ct);
        return NoContent();
    }
}
