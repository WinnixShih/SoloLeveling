using Microsoft.EntityFrameworkCore;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Domain.Rules;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Services;

/// <summary>
/// 玩家總覽與設定。
/// </summary>
/// <param name="db">DbContext。</param>
/// <param name="loader">今日內容載入（含結算）。</param>
/// <param name="clock">時間來源。</param>
public class PlayerService(AppDbContext db, TodayContextLoader loader, TimeProvider clock)
{
    /// <summary>
    /// GET /me：先結算再回傳總覽。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>總覽。</returns>
    public async Task<MeResponse> GetMeAsync(Guid userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        var response = await BuildAsync(context, ct);
        await tx.CommitAsync(ct);
        return response;
    }

    /// <summary>
    /// PATCH /me：更新顯示名稱、時區、困難模式；困難模式變更會立即重算今日達標與獎勵。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="request">要更新的欄位。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>更新後的總覽。</returns>
    public async Task<MeResponse> PatchMeAsync(Guid userId, PatchMeRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);

        if (request.DisplayName is { } displayName)
        {
            var trimmed = displayName.Trim();
            if (trimmed.Length is 0 or > 40)
            {
                throw Errors.ApiErrorException.BadRequest("InvalidDisplayName", "顯示名稱需為 1–40 字");
            }

            context.User.DisplayName = trimmed;
        }

        if (request.TimeZoneId is { } timeZoneId)
        {
            context.User.TimeZoneId = AccountService.ValidateTimeZone(timeZoneId);
        }

        if (request.HardMode is { } hardMode && hardMode != context.Player.HardMode)
        {
            context.Player.HardMode = hardMode;
            var events = ProgressUpdater.Recalculate(context.Player, context.TodayLog, context.ActiveQuests, clock.GetUtcNow());
            db.XpEvents.AddRange(events);
        }

        await db.SaveChangesAsync(ct);
        var response = await BuildAsync(context, ct);
        await tx.CommitAsync(ct);
        return response;
    }

    private async Task<MeResponse> BuildAsync(TodayContext context, CancellationToken ct)
    {
        var program = await db.Programs.AsNoTracking().SingleAsync(p => p.UserId == context.User.Id && p.IsActive, ct);
        return new MeResponse(context.User.ToDto(), context.Player.ToDto(context.TodayLog.IsCleared), program.ToDto(context.Today), context.ActiveQuests.Count == 0);
    }
}
