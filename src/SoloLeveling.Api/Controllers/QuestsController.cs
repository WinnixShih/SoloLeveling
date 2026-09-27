using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoloLeveling.Api.Auth;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Services;

namespace SoloLeveling.Api.Controllers;

/// <summary>
/// 任務管理。
/// </summary>
/// <param name="quests">任務服務。</param>
[ApiController]
[Authorize]
[Route("api/v1/quests")]
public class QuestsController(QuestService quests) : ControllerBase
{
    /// <summary>
    /// 未封存的任務清單，依顯示順序排序。
    /// </summary>
    /// <param name="ct">取消權杖。</param>
    /// <returns>任務清單。</returns>
    [HttpGet]
    [ProducesResponseType<List<QuestDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<QuestDto>>> List(CancellationToken ct)
    {
        return await quests.ListAsync(User.GetUserId(), ct);
    }

    /// <summary>
    /// 新增任務；Count／Limit 類型必須提供 targetValue。
    /// </summary>
    /// <param name="request">任務內容。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>201 與新任務。</returns>
    [HttpPost]
    [ProducesResponseType<QuestDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<QuestDto>> Create(QuestRequest request, CancellationToken ct)
    {
        var created = await quests.CreateAsync(User.GetUserId(), request, ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>
    /// 重設任務顯示順序；questIds 必須恰好包含全部未封存任務。
    /// </summary>
    /// <param name="request">新順序。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>204。</returns>
    [HttpPut("reorder")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Reorder(ReorderRequest request, CancellationToken ct)
    {
        await quests.ReorderAsync(User.GetUserId(), request.QuestIds, ct);
        return NoContent();
    }

    /// <summary>
    /// 修改任務；改任務類型會清除該任務今日進度。
    /// </summary>
    /// <param name="id">任務 ID。</param>
    /// <param name="request">任務內容。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>更新後的任務。</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<QuestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuestDto>> Update(Guid id, QuestRequest request, CancellationToken ct)
    {
        return await quests.UpdateAsync(User.GetUserId(), id, request, ct);
    }

    /// <summary>
    /// 封存任務（不刪列）。
    /// </summary>
    /// <param name="id">任務 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>204。</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await quests.ArchiveAsync(User.GetUserId(), id, ct);
        return NoContent();
    }
}
