using Microsoft.EntityFrameworkCore;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Infrastructure;

/// <summary>
/// 結算服務：在交易內鎖住 Player 列（<c>SELECT … FOR UPDATE</c>），載入待結算區間的紀錄，套用 <see cref="Settlement"/> 規則後寫回。
/// 同一使用者併發呼叫時，第二個會等第一個 commit 後才讀到已更新的 LastSettledDate，因此不會重複懲罰。
/// </summary>
/// <param name="db">DbContext；若呼叫端已開啟交易，會沿用該交易，不另開。</param>
/// <param name="clock">時間來源。</param>
public class SettlementService(AppDbContext db, TimeProvider clock)
{
    /// <summary>
    /// 對指定使用者執行結算，並確保今日的 <see cref="Domain.Entities.DailyLog"/> 存在。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>結算結果；<see cref="SettlementResult.TodayLog"/> 已被 DbContext 追蹤，呼叫端可直接繼續修改。</returns>
    public async Task<SettlementResult> SettleAsync(Guid userId, CancellationToken ct)
    {
        var ownsTransaction = db.Database.CurrentTransaction is null;
        var transaction = ownsTransaction ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            var player = await db.Players
                .FromSqlInterpolated($"SELECT * FROM \"Players\" WHERE \"UserId\" = {userId} FOR UPDATE")
                .SingleAsync(ct);
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);

            var now = clock.GetUtcNow();
            var today = UserClock.DateOf(now, user.TimeZoneId);
            var start = player.LastSettledDate is { } last
                ? last.AddDays(1)
                : UserClock.DateOf(player.CreatedAt, user.TimeZoneId);
            var loadFrom = start < today.AddDays(-Settlement.MaxCatchUpDays) ? today.AddDays(-Settlement.MaxCatchUpDays) : start;

            var logs = await db.DailyLogs
                .Where(l => l.UserId == userId && l.Date >= loadFrom && l.Date <= today)
                .ToListAsync(ct);

            var result = Settlement.Settle(player, user.TimeZoneId, logs, now);

            db.DailyLogs.AddRange(result.NewLogs);
            db.XpEvents.AddRange(result.Events);
            await db.SaveChangesAsync(ct);

            if (transaction is not null)
            {
                await transaction.CommitAsync(ct);
            }

            return result;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }
}
