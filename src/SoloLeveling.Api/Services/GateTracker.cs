using Microsoft.EntityFrameworkCore;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Services;

/// <summary>
/// 進行中的門與目前已漏的天數。
/// </summary>
/// <param name="Gate">門（已被 DbContext 追蹤）。</param>
/// <param name="Misses">門期間內已結算且未達標的天數。</param>
public sealed record ActiveGateState(Gate Gate, int Misses);

/// <summary>
/// 一次傳送門判定的結果。
/// </summary>
/// <param name="Cleared">本次通關的等級；沒有為 null。</param>
/// <param name="Failed">本次失敗的等級；沒有為 null。</param>
/// <param name="Active">判定後仍在進行中的門；沒有為 null。</param>
public sealed record GateResolution(GateRank? Cleared, GateRank? Failed, ActiveGateState? Active)
{
    /// <summary>沒有進行中的門，本次也沒有門結束。</summary>
    public static GateResolution None { get; } = new(null, null, null);
}

/// <summary>
/// 傳送門的查詢與判定。已漏天數每次由 DailyLogs 一次 COUNT 算出，不存欄位；通關或失敗一經寫入就不再判定。
/// 必須在呼叫端開啟的交易內、<see cref="TodayContextLoader.LoadAsync"/> 之後使用。
/// </summary>
/// <param name="db">DbContext。</param>
/// <param name="clock">時間來源。</param>
public class GateTracker(AppDbContext db, TimeProvider clock)
{
    /// <summary>
    /// 載入進行中的門與已漏天數（兩次查詢，不隨天數增加）。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>進行中的門；沒有為 null。</returns>
    public async Task<ActiveGateState?> LoadActiveAsync(Guid userId, CancellationToken ct)
    {
        var gate = await db.Gates.SingleOrDefaultAsync(g => g.UserId == userId && g.Status == GateStatus.Active, ct);
        if (gate is null)
        {
            return null;
        }

        var startDate = gate.StartDate;
        var lastDay = GateRules.LastDay(gate);
        var misses = await db.DailyLogs.CountAsync(
            l => l.UserId == userId && l.Date >= startDate && l.Date <= lastDay && l.IsSettled && !l.IsCleared,
            ct);
        return new ActiveGateState(gate, misses);
    }

    /// <summary>
    /// 判定進行中的門；通關或失敗時寫入狀態與 <see cref="Gate.EndedAt"/> 並立即 SaveChanges，讓之後的統計查詢看得到本次通關。
    /// </summary>
    /// <param name="context">今日內容（今日達標狀態已反映本次修改）。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>判定結果；沒有進行中的門回 <see cref="GateResolution.None"/>。</returns>
    public async Task<GateResolution> ResolveAsync(TodayContext context, CancellationToken ct)
    {
        var active = await LoadActiveAsync(context.User.Id, ct);
        if (active is null)
        {
            return GateResolution.None;
        }

        var verdict = GateRules.Judge(active.Gate, context.Today, active.Misses, context.TodayLog.IsCleared);
        if (verdict == GateVerdict.InProgress)
        {
            return new GateResolution(null, null, active);
        }

        active.Gate.Status = verdict == GateVerdict.Cleared ? GateStatus.Cleared : GateStatus.Failed;
        active.Gate.EndedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return verdict == GateVerdict.Cleared
            ? new GateResolution(active.Gate.Rank, null, null)
            : new GateResolution(null, active.Gate.Rank, null);
    }

    /// <summary>
    /// 各等級的通關次數（一次查詢）。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>等級對通關次數；沒有通關的等級不在字典內。</returns>
    public async Task<Dictionary<GateRank, int>> LoadClearCountsAsync(Guid userId, CancellationToken ct)
    {
        return await db.Gates
            .Where(g => g.UserId == userId && g.Status == GateStatus.Cleared)
            .GroupBy(g => g.Rank)
            .Select(g => new { Rank = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Rank, x => x.Count, ct);
    }
}
