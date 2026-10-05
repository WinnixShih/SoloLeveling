using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoloLeveling.Api.Auth;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Services;

namespace SoloLeveling.Api.Controllers;

/// <summary>
/// 獎勵總覽、開箱、圖鑑與商店。
/// </summary>
/// <param name="rewards">獎勵服務。</param>
[ApiController]
[Authorize]
[Route("api/v1")]
public class RewardsController(RewardService rewards) : ControllerBase
{
    /// <summary>
    /// 金幣、保險卡、未開寶箱、成就與進度、稱號字塊、釘選卡與主題。
    /// </summary>
    /// <param name="ct">取消權杖。</param>
    /// <returns>總覽。</returns>
    [HttpGet("rewards")]
    [ProducesResponseType<RewardsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RewardsResponse>> Get(CancellationToken ct)
    {
        return await rewards.GetAsync(User.GetUserId(), ct);
    }

    /// <summary>
    /// 開啟一個未開寶箱。
    /// </summary>
    /// <param name="id">寶箱 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>掉落卡片、是否重複、金幣與餘額。</returns>
    [HttpPost("rewards/chests/{id:guid}/open")]
    [ProducesResponseType<OpenChestResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OpenChestResponse>> OpenChest(Guid id, CancellationToken ct)
    {
        return await rewards.OpenChestAsync(User.GetUserId(), id, ct);
    }

    /// <summary>
    /// 卡片目錄與擁有狀態。
    /// </summary>
    /// <param name="ct">取消權杖。</param>
    /// <returns>圖鑑。</returns>
    [HttpGet("cards")]
    [ProducesResponseType<CardsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CardsResponse>> GetCards(CancellationToken ct)
    {
        return await rewards.GetCardsAsync(User.GetUserId(), ct);
    }

    /// <summary>
    /// 購買連勝保險卡、E 級寶箱或主題。
    /// </summary>
    /// <param name="request">商品與主題鍵。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>購買結果。</returns>
    [HttpPost("shop/purchase")]
    [ProducesResponseType<PurchaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PurchaseResponse>> Purchase(PurchaseRequest request, CancellationToken ct)
    {
        return await rewards.PurchaseAsync(User.GetUserId(), request, ct);
    }
}
