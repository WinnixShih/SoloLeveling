using Microsoft.EntityFrameworkCore;
using Npgsql;
using SoloLeveling.Domain.Rules;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Services;

/// <summary>
/// 每小時掃一次，對「LastSettledDate 落後於各自今日 - 1」的使用者執行結算，讓沒開 App 的人也會被扣懲罰、歸零連續紀錄。
/// 每位使用者用獨立的 scope 與交易，一人失敗不影響其他人。
/// </summary>
/// <param name="scopeFactory">建立每位使用者的 DI scope。</param>
/// <param name="clock">時間來源。</param>
/// <param name="logger">記錄器。</param>
public class SettlementScheduler(IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<SettlementScheduler> logger) : BackgroundService
{
    /// <summary>掃描間隔。</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>
    /// 啟動後立刻跑一次，之後每小時一次。
    /// </summary>
    /// <param name="stoppingToken">停止權杖。</param>
    /// <returns>非同步作業。</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// 掃描並結算所有落後的使用者。
    /// </summary>
    /// <param name="ct">取消權杖。</param>
    /// <returns>非同步作業。</returns>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        List<Guid> userIds;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var candidates = await db.Users.AsNoTracking()
                .Join(db.Players.AsNoTracking(), u => u.Id, p => p.UserId, (u, p) => new { u.Id, u.TimeZoneId, p.LastSettledDate })
                .ToListAsync(ct);
            // 「今日」依各使用者時區而異，篩選在記憶體做
            userIds = candidates
                .Where(c => c.LastSettledDate is null || c.LastSettledDate < UserClock.DateOf(now, c.TimeZoneId).AddDays(-1))
                .Select(c => c.Id)
                .ToList();
        }

        foreach (var userId in userIds)
        {
            using var scope = scopeFactory.CreateScope();
            var settlement = scope.ServiceProvider.GetRequiredService<SettlementService>();
            try
            {
                await settlement.SettleAsync(userId, ct);
            }
            catch (Exception ex) when (ex is DbUpdateException or NpgsqlException or TimeoutException)
            {
                logger.LogError(ex, "排程結算使用者 {UserId} 失敗", userId);
            }
        }
    }
}
