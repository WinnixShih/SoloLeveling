using Microsoft.EntityFrameworkCore;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;
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
/// <param name="DoneDaysBeforeToday">每個漸進任務在今天之前的達標天數；一般任務不在字典內。</param>
/// <param name="SettledDays">本次請求的結算新結算了幾天；0 代表玩家狀態沒有因結算而改變。</param>
/// <param name="Before">結算後、任何修改前的玩家快照（在列鎖內拍；結算不改等級，等同請求前的值），供獎勵回應的 levelsGained 使用。</param>
public sealed record TodayContext(
    User User,
    Player Player,
    DailyLog TodayLog,
    List<Quest> ActiveQuests,
    DateOnly Today,
    IReadOnlyDictionary<Guid, int> DoneDaysBeforeToday,
    int SettledDays,
    PlayerSnapshot Before)
{
    /// <summary>
    /// 某任務在今天之前的達標天數；不在字典內（一般任務或新任務）回 0。
    /// </summary>
    /// <param name="questId">任務 ID。</param>
    /// <returns>達標天數。</returns>
    public int DoneDaysOf(Guid questId)
    {
        return DoneDaysBeforeToday.TryGetValue(questId, out var days) ? days : 0;
    }
}

/// <summary>
/// 先結算、再把今日需要的實體一次載入。必須在呼叫端開啟的交易內使用，結算會沿用該交易。
/// </summary>
/// <param name="db">DbContext。</param>
/// <param name="settlement">結算服務。</param>
public class TodayContextLoader(AppDbContext db, SettlementService settlement)
{
    /// <summary>
    /// 結算並載入今日內容。漸進任務的達標天數一次批次查出，不逐任務查。
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

        var progressionIds = activeQuests.Where(q => q.GoalId != null).Select(q => q.Id).ToList();
        var doneDays = await LoadDoneDaysAsync(progressionIds, settled.Today, ct);

        return new TodayContext(user, player, settled.TodayLog, activeQuests, settled.Today, doneDays, settled.SettledDays, new PlayerSnapshot(player.Level));
    }

    /// <summary>
    /// 批次查出指定任務在今天之前的達標天數（一次查詢）；沒有達標紀錄的任務不在回傳字典內。
    /// </summary>
    /// <param name="questIds">任務 ID；可含已封存的任務。</param>
    /// <param name="today">使用者時區的今日。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>任務 ID 對達標天數的字典。</returns>
    public async Task<Dictionary<Guid, int>> LoadDoneDaysAsync(IReadOnlyCollection<Guid> questIds, DateOnly today, CancellationToken ct)
    {
        if (questIds.Count == 0)
        {
            return [];
        }

        return await db.QuestProgresses
            .Join(db.DailyLogs, p => p.DailyLogId, l => l.Id, (p, l) => new { p.QuestId, p.IsDone, l.Date })
            .Where(x => x.IsDone && x.Date < today && questIds.Contains(x.QuestId))
            .GroupBy(x => x.QuestId)
            .Select(g => new { QuestId = g.Key, Days = g.Count() })
            .ToDictionaryAsync(x => x.QuestId, x => x.Days, ct);
    }
}
