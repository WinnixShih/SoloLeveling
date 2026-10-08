using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Errors;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Services;

/// <summary>
/// 傳送門：列出等級、開門、放棄。開門與放棄前都先判定進行中的門，結算後剛通關或失敗的門不會擋住開新門，判定結果也會在同一個回應公告。
/// </summary>
/// <param name="db">DbContext。</param>
/// <param name="loader">今日內容載入（含結算與列鎖）。</param>
/// <param name="rewardApplier">獎勵判定與持久化。</param>
/// <param name="gateTracker">傳送門查詢與判定。</param>
/// <param name="clock">時間來源。</param>
public class GateService(AppDbContext db, TodayContextLoader loader, RewardApplier rewardApplier, GateTracker gateTracker, TimeProvider clock)
{
    /// <summary>
    /// GET /gates：先結算並依讀取規則補判定，再列出六級門與進行中的門。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>等級表、進行中的門與本次獎勵。</returns>
    public async Task<GatesResponse> ListAsync(Guid userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        var rewards = await rewardApplier.ApplyOnReadAsync(context, ct);
        var active = await gateTracker.LoadActiveAsync(userId, ct);
        var clearCounts = await gateTracker.LoadClearCountsAsync(userId, ct);
        await tx.CommitAsync(ct);
        return new GatesResponse(
            Gates.All.Select(g => g.ToDto(clearCounts)).ToList(),
            active?.Gate.ToDto(context.Today, active.Misses),
            rewards);
    }

    /// <summary>
    /// POST /gates：從今日開一道已解鎖的門。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="rank">等級。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>新開的門（含本次獎勵）。</returns>
    /// <exception cref="ApiErrorException">等級缺漏或未定義（400 <c>InvalidGateRank</c>）、已有進行中的門（409 <c>GateInProgress</c>）、未解鎖（400 <c>GateLocked</c>）。</exception>
    public async Task<ActiveGateDto> OpenAsync(Guid userId, GateRank? rank, CancellationToken ct)
    {
        if (rank is not { } value || !Enum.IsDefined(value))
        {
            throw ApiErrorException.BadRequest("InvalidGateRank", "傳送門等級需為 E、D、C、B、A、S 其中之一");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        var resolution = await gateTracker.ResolveAsync(context, ct);
        if (resolution.Active is not null)
        {
            throw ApiErrorException.Conflict("GateInProgress", "已有進行中的傳送門");
        }

        var definition = Gates.Of(value);
        var clearCounts = await gateTracker.LoadClearCountsAsync(userId, ct);
        if (!Gates.IsUnlocked(value, clearCounts))
        {
            throw ApiErrorException.BadRequest("GateLocked", $"需先通關 {definition.RequiredRank} 級傳送門");
        }

        var gate = new Gate
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Rank = value,
            StartDate = context.Today,
            LengthDays = definition.LengthDays,
            AllowedMisses = definition.AllowedMisses,
            Status = GateStatus.Active,
            CreatedAt = clock.GetUtcNow(),
        };
        db.Gates.Add(gate);
        await db.SaveChangesAsync(ct);
        var rewards = await rewardApplier.ApplyAsync(context, ct, resolvedGate: resolution);
        await tx.CommitAsync(ct);
        return gate.ToDto(context.Today, 0) with { Rewards = rewards };
    }

    /// <summary>
    /// POST /gates/active/abandon：放棄進行中的門（沒有懲罰，可立刻重開）。門若已在判定中通關或失敗，以判定結果為準、不改成放棄。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>本次獎勵。</returns>
    /// <exception cref="ApiErrorException">沒有進行中的門（404 <c>NoActiveGate</c>）。</exception>
    public async Task<RewardsEnvelope> AbandonAsync(Guid userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        var resolution = await gateTracker.ResolveAsync(context, ct);
        if (resolution.Active is null && resolution.Cleared is null && resolution.Failed is null)
        {
            throw ApiErrorException.NotFound("NoActiveGate", "目前沒有進行中的傳送門");
        }

        if (resolution.Active is { } active)
        {
            active.Gate.Status = GateStatus.Abandoned;
            active.Gate.EndedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }

        var rewards = await rewardApplier.ApplyAsync(context, ct, resolvedGate: resolution with { Active = null });
        await tx.CommitAsync(ct);
        return new RewardsEnvelope(rewards);
    }
}
