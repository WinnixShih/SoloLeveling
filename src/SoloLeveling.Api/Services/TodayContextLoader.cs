using Microsoft.EntityFrameworkCore;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Services;

/// <summary>
/// 結算後的「今日」工作內容；所有實體皆由 DbContext 追蹤，修改後 SaveChanges 即可。
/// </summary>
/// <param name="User">使用者。</param>
/// <param name="Player">玩家。</param>
/// <param name="TodayLog">今日紀錄，<see cref="DailyLog.Progresses"/> 已載入。</param>
/// <param name="ActiveQuests">未封存任務，依 SortOrder 排序。</param>
/// <param name="Today">使用者時區的今日。</param>
public sealed record TodayContext(User User, Player Player, DailyLog TodayLog, List<Quest> ActiveQuests, DateOnly Today);

/// <summary>
/// 先結算、再把今日需要的實體一次載入。必須在呼叫端開啟的交易內使用，結算會沿用該交易。
/// </summary>
/// <param name="db">DbContext。</param>
/// <param name="settlement">結算服務。</param>
public class TodayContextLoader(AppDbContext db, SettlementService settlement)
{
    /// <summary>
    /// 結算並載入今日內容。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>今日內容。</returns>
    public async Task<TodayContext> LoadAsync(Guid userId, CancellationToken ct)
    {
        var settled = await settlement.SettleAsync(userId, ct);
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        var player = await db.Players.SingleAsync(p => p.UserId == userId, ct);
        // 今日紀錄已被追蹤，查出的進度會由 EF 自動掛回 Progresses
        await db.QuestProgresses.Where(p => p.DailyLogId == settled.TodayLog.Id).LoadAsync(ct);
        var activeQuests = await db.Quests
            .Where(q => q.UserId == userId && !q.IsArchived)
            .OrderBy(q => q.SortOrder)
            .ToListAsync(ct);
        return new TodayContext(user, player, settled.TodayLog, activeQuests, settled.Today);
    }
}
