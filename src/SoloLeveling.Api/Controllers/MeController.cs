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

    /// <summary>
    /// 設定稱號組合；只能選已解鎖且槽位正確的字塊，可只選一邊或都不選。
    /// </summary>
    /// <param name="request">前綴與後綴字塊鍵，null 表示不選。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>更新後的總覽。</returns>
    [HttpPut("title")]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MeResponse>> SetTitle(SetTitleRequest request, CancellationToken ct)
    {
        return await players.SetTitleAsync(User.GetUserId(), request, ct);
    }

    /// <summary>
    /// 釘選一張已擁有的卡片；cardId 為 null 時取消。
    /// </summary>
    /// <param name="request">卡片 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>更新後的總覽。</returns>
    [HttpPut("pinned-card")]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MeResponse>> SetPinnedCard(SetPinnedCardRequest request, CancellationToken ct)
    {
        return await players.SetPinnedCardAsync(User.GetUserId(), request, ct);
    }

    /// <summary>
    /// 切換到已擁有的主題。
    /// </summary>
    /// <param name="request">主題鍵。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>更新後的總覽。</returns>
    [HttpPut("theme")]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MeResponse>> SetTheme(SetThemeRequest request, CancellationToken ct)
    {
        return await players.SetThemeAsync(User.GetUserId(), request, ct);
    }
}
