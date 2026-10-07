using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Errors;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Rules;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Services;

/// <summary>
/// 玩家總覽與設定。
/// </summary>
/// <param name="db">DbContext。</param>
/// <param name="loader">今日內容載入（含結算）。</param>
/// <param name="rewardApplier">獎勵判定與持久化。</param>
/// <param name="clock">時間來源。</param>
public class PlayerService(AppDbContext db, TodayContextLoader loader, RewardApplier rewardApplier, TimeProvider clock)
{
    /// <summary>
    /// GET /me：先結算、套用獎勵，再回傳總覽。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>總覽。</returns>
    public async Task<MeResponse> GetMeAsync(Guid userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        return await SaveAndBuildAsync(context, tx, ct, isRead: true);
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
                throw ApiErrorException.BadRequest("InvalidDisplayName", "顯示名稱需為 1–40 字");
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

        return await SaveAndBuildAsync(context, tx, ct);
    }

    /// <summary>
    /// PUT /me/title：設定稱號前綴與後綴；可只選一邊或都不選。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="request">前綴與後綴字塊鍵。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>更新後的總覽。</returns>
    /// <exception cref="ApiErrorException">字塊未解鎖或槽位不符（400，<c>TitleNotUnlocked</c>）。</exception>
    public async Task<MeResponse> SetTitleAsync(Guid userId, SetTitleRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        var unlocked = await db.Achievements.Where(a => a.UserId == userId).Select(a => a.Key).ToListAsync(ct);
        EnsureFragment(request.PrefixKey, TitleSlot.Prefix, unlocked);
        EnsureFragment(request.SuffixKey, TitleSlot.Suffix, unlocked);
        context.Player.TitlePrefixKey = request.PrefixKey;
        context.Player.TitleSuffixKey = request.SuffixKey;
        return await SaveAndBuildAsync(context, tx, ct);
    }

    /// <summary>
    /// PUT /me/pinned-card：從已擁有的卡中釘選一張，傳 null 取消。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="request">卡片 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>更新後的總覽。</returns>
    /// <exception cref="ApiErrorException">尚未擁有或不存在的卡（400，<c>CardNotOwned</c>）。</exception>
    public async Task<MeResponse> SetPinnedCardAsync(Guid userId, SetPinnedCardRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        if (request.CardId is { } cardId && !await db.OwnedCards.AnyAsync(c => c.UserId == userId && c.CardId == cardId, ct))
        {
            throw ApiErrorException.BadRequest("CardNotOwned", "尚未擁有這張卡片");
        }

        context.Player.PinnedCardId = request.CardId;
        return await SaveAndBuildAsync(context, tx, ct);
    }

    /// <summary>
    /// PUT /me/theme：切換到已擁有的主題（azure 永遠可用）。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="request">主題鍵。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>更新後的總覽。</returns>
    /// <exception cref="ApiErrorException">尚未擁有或不存在的主題（400，<c>ThemeNotOwned</c>）。</exception>
    public async Task<MeResponse> SetThemeAsync(Guid userId, SetThemeRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        var key = request.ThemeKey;
        var owned = key == Themes.Default || await db.OwnedThemes.AnyAsync(t => t.UserId == userId && t.ThemeKey == key, ct);
        if (!owned)
        {
            throw ApiErrorException.BadRequest("ThemeNotOwned", "尚未擁有此主題");
        }

        context.Player.ThemeKey = key;
        return await SaveAndBuildAsync(context, tx, ct);
    }

    /// <summary>
    /// 字塊必須已解鎖且位置正確；null 表示不選，直接通過。
    /// </summary>
    /// <param name="key">字塊鍵。</param>
    /// <param name="slot">要放的位置。</param>
    /// <param name="unlocked">已解鎖的成就鍵。</param>
    /// <exception cref="ApiErrorException">未解鎖或位置不符（400，<c>TitleNotUnlocked</c>）。</exception>
    private static void EnsureFragment(string? key, TitleSlot slot, List<string> unlocked)
    {
        if (key is null)
        {
            return;
        }

        if (!unlocked.Contains(key) || Achievements.Find(key)?.Slot != slot)
        {
            throw ApiErrorException.BadRequest("TitleNotUnlocked", "稱號字塊尚未解鎖或位置不符");
        }
    }

    /// <summary>
    /// 存檔、套用獎勵、組回應並 commit；所有回傳 <see cref="MeResponse"/> 的端點共用。
    /// </summary>
    /// <param name="context">今日內容（已套用本次修改）。</param>
    /// <param name="tx">呼叫端開的交易。</param>
    /// <param name="ct">取消權杖。</param>
    /// <param name="isRead">true 表示純讀取（GET /me），無新結算時略過獎勵判定。</param>
    /// <returns>總覽（含本次獎勵）。</returns>
    private async Task<MeResponse> SaveAndBuildAsync(TodayContext context, IDbContextTransaction tx, CancellationToken ct, bool isRead = false)
    {
        await db.SaveChangesAsync(ct);
        var rewards = isRead
            ? await rewardApplier.ApplyOnReadAsync(context, ct)
            : await rewardApplier.ApplyAsync(context, 0, ct);
        var program = await db.Programs.AsNoTracking().SingleAsync(p => p.UserId == context.User.Id && p.IsActive, ct);
        await tx.CommitAsync(ct);
        return new MeResponse(
            context.User.ToDto(),
            context.Player.ToDto(context.TodayLog.IsCleared),
            program.ToDto(context.Today),
            context.ActiveQuests.Count == 0,
            rewards);
    }
}
