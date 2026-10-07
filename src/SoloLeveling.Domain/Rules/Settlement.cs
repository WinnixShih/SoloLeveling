using SoloLeveling.Domain.Entities;

namespace SoloLeveling.Domain.Rules;

/// <summary>
/// 一次結算的結果；新建的紀錄與事件由呼叫端持久化。
/// </summary>
/// <param name="Today">使用者時區下的今日。</param>
/// <param name="TodayLog">今日紀錄（可能是既有的或這次新建的）。</param>
/// <param name="NewLogs">這次新建的 <see cref="DailyLog"/>（含補建的缺席日與今日）。</param>
/// <param name="Events">這次產生的 EXP 事件（只會有 Penalty）。</param>
/// <param name="ShieldsUsed">這次消耗保險卡保護的日期，由舊到新；呼叫端據此寫 <c>RewardEvent</c>。</param>
/// <param name="SettledDays">這次新結算的天數；0 代表玩家狀態沒有因結算而改變。</param>
public record SettlementResult(DateOnly Today, DailyLog TodayLog, IReadOnlyList<DailyLog> NewLogs, IReadOnlyList<XpEvent> Events, IReadOnlyList<DateOnly> ShieldsUsed, int SettledDays);

/// <summary>
/// 結算演算法（規格第 7 節）的純規則部分：把 [start, today) 的每一天轉成已結算的 <see cref="DailyLog"/>，更新 Streak 與懲罰。
/// 交易與列鎖由 Infrastructure 的服務負責。
/// </summary>
public static class Settlement
{
    /// <summary>連續缺席超過此天數時，只逐日結算最近這麼多天，更早的一次性歸零 Streak。</summary>
    public const int MaxCatchUpDays = 400;

    /// <summary>同一次結算最多懲罰的天數。</summary>
    public const int MaxPenaltiesPerSettlement = 3;

    /// <summary>
    /// 執行結算。
    /// </summary>
    /// <param name="player">玩家；Streak、BestStreak、Xp、ShieldCount、LastSettledDate 會被更新。未達標日若連勝進行中且持有保險卡，消耗 1 張讓連勝延續（困難模式懲罰照扣）。</param>
    /// <param name="timeZoneId">使用者的 IANA 時區 ID。</param>
    /// <param name="existingLogs">該使用者已存在的紀錄（至少要涵蓋待結算區間與今日）。</param>
    /// <param name="now">當下時間（UTC）。</param>
    /// <returns>結算結果。</returns>
    public static SettlementResult Settle(Player player, string timeZoneId, IReadOnlyCollection<DailyLog> existingLogs, DateTimeOffset now)
    {
        var today = UserClock.DateOf(now, timeZoneId);
        var logsByDate = existingLogs.ToDictionary(l => l.Date);
        var newLogs = new List<DailyLog>();
        var events = new List<XpEvent>();
        var shieldsUsed = new List<DateOnly>();

        var start = player.LastSettledDate is { } last
            ? last.AddDays(1)
            : UserClock.DateOf(player.CreatedAt, timeZoneId);

        if (today.DayNumber - start.DayNumber > MaxCatchUpDays)
        {
            player.Streak = 0;
            start = today.AddDays(-MaxCatchUpDays);
        }

        var penaltiesApplied = 0;
        var settledDays = 0;
        for (var date = start; date < today; date = date.AddDays(1))
        {
            if (!logsByDate.TryGetValue(date, out var log))
            {
                log = NewLog(player.UserId, date, now);
                logsByDate[date] = log;
                newLogs.Add(log);
            }

            if (log.IsSettled)
            {
                continue;
            }

            // CompletionRatio 沿用 log 現值：進度只能寫「今日」，日終時該值即為當日最終達標率
            log.IsCleared = log.CompletionRatio >= CompletionRules.ThresholdOf(player.HardMode);
            if (log.IsCleared)
            {
                player.Streak += 1;
            }
            else
            {
                // 保險卡只保護進行中的連勝；Streak 為 0 時沒有東西可保護，不消耗
                if (player.ShieldCount > 0 && player.Streak > 0)
                {
                    player.ShieldCount -= 1;
                    player.Streak += 1;
                    shieldsUsed.Add(date);
                }
                else
                {
                    player.Streak = 0;
                }

                if (player.HardMode && penaltiesApplied < MaxPenaltiesPerSettlement)
                {
                    var penalty = CompletionRules.PenaltyOf(player.Level);
                    Leveling.ApplyPenalty(player, penalty);
                    events.Add(new XpEvent
                    {
                        Id = Guid.NewGuid(),
                        UserId = player.UserId,
                        Amount = -penalty,
                        Source = XpSource.Penalty,
                        RefId = log.Id,
                        OccurredAt = now,
                    });
                    penaltiesApplied += 1;
                }
            }

            player.BestStreak = Math.Max(player.BestStreak, player.Streak);
            log.IsSettled = true;
            log.SettledAt = now;
            settledDays += 1;
        }

        var yesterday = today.AddDays(-1);
        // 改時區往西時今日可能倒退，LastSettledDate 只增不減、不重跑已結算的日子
        if (player.LastSettledDate is null || player.LastSettledDate < yesterday)
        {
            player.LastSettledDate = yesterday;
        }

        if (!logsByDate.TryGetValue(today, out var todayLog))
        {
            todayLog = NewLog(player.UserId, today, now);
            newLogs.Add(todayLog);
        }

        return new SettlementResult(today, todayLog, newLogs, events, shieldsUsed, settledDays);
    }

    private static DailyLog NewLog(Guid userId, DateOnly date, DateTimeOffset now)
    {
        return new DailyLog { Id = Guid.NewGuid(), UserId = userId, Date = date, CreatedAt = now };
    }
}
