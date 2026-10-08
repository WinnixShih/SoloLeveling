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
/// <param name="Misses">門期間內已結算日子中未達標的天數（沒有紀錄的日子也算）。</param>
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
/// 傳送門的查詢與判定。已漏天數每次由已結算天數減去 DailyLogs 一次 COUNT 的達標天數算出，不存欄位；通關或失敗一經寫入就不再判定。
/// 必須在呼叫端開啟的交易內、<see cref="TodayContextLoader.LoadAsync"/> 之後使用。
/// </summary>
/// <param name="db">DbContext。</param>
/// <param name="clock">時間來源。</param>
public class GateTracker(AppDbContext db, TimeProvider clock)
{
    /// <summary>
    /// 載入進行中的門與已漏天數（兩次查詢，不隨天數增加）。
    /// 已漏天數 = 門期間內已結算的天數 − 已結算且達標的紀錄數：長期未登入時結算只補跑最近
    /// <see cref="Settlement.MaxCatchUpDays"/> 天，更早的日子沒有紀錄，必須當成漏掉，否則會誤判通關。
    /// 今日尚未結算，不列入。
    /// </summary>
    /// <param name="player">玩家（須已結算，<see cref="Player.LastSettledDate"/> 為最新值）。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>進行中的門；沒有為 null。</returns>
    public async Task<ActiveGateState?> LoadActiveAsync(Player player, CancellationToken ct)
    {
        var userId = player.UserId;
        var gate = await db.Gates.SingleOrDefaultAsync(g => g.UserId == userId && g.Status == GateStatus.Active, ct);
        if (gate is null)
        {
            return null;
        }

        var startDate = gate.StartDate;
        var lastDay = GateRules.LastDay(gate);
        if (player.LastSettledDate is not { } lastSettled || lastSettled < startDate)
        {
            return new ActiveGateState(gate, 0);
        }

        var settledEnd = lastSettled < lastDay ? lastSettled : lastDay;
        var settledDays = settledEnd.DayNumber - startDate.DayNumber + 1;
        var clearedDays = await db.DailyLogs.CountAsync(
            l => l.UserId == userId && l.Date >= startDate && l.Date <= settledEnd && l.IsSettled && l.IsCleared,
            ct);
        return new ActiveGateState(gate, settledDays - clearedDays);
    }

    /// <summary>
    /// 判定進行中的門；通關或失敗時寫入狀態與 <see cref="Gate.EndedAt"/> 並立即 SaveChanges，讓之後的統計查詢看得到本次通關。
    /// </summary>
    /// <param name="context">今日內容（今日達標狀態已反映本次修改）。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>判定結果；沒有進行中的門回 <see cref="GateResolution.None"/>。</returns>
    public async Task<GateResolution> ResolveAsync(TodayContext context, CancellationToken ct)
    {
        var active = await LoadActiveAsync(context.Player, ct);
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
