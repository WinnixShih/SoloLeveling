using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Tests;

[Collection(PostgresCollection.Name)]
public class SettlementServiceTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 28);
    private static readonly DateOnly Yesterday = new(2026, 9, 27);

    private async Task<Guid> SeedUserAsync(string timeZoneId, DateOnly? lastSettled, bool hardMode = false, int streak = 0, int level = 1, int xp = 0)
    {
        await using var db = fixture.CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@test.local", PasswordHash = "x", DisplayName = "t", TimeZoneId = timeZoneId, CreatedAt = Now.AddDays(-30) };
        db.Users.Add(user);
        db.Players.Add(new Player { UserId = user.Id, LastSettledDate = lastSettled, HardMode = hardMode, Streak = streak, Level = level, Xp = xp, CreatedAt = user.CreatedAt });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task SeedLogAsync(Guid userId, DateOnly date, decimal ratio)
    {
        await using var db = fixture.CreateDbContext();
        db.DailyLogs.Add(new DailyLog { Id = Guid.NewGuid(), UserId = userId, Date = date, CompletionRatio = ratio, CreatedAt = Now.AddDays(-1) });
        await db.SaveChangesAsync();
    }

    private async Task SettleAsync(Guid userId, DateTimeOffset now)
    {
        await using var db = fixture.CreateDbContext();
        var service = new SettlementService(db, new FakeTimeProvider(now));
        await service.SettleAsync(userId, CancellationToken.None);
    }

    private async Task<Player> LoadPlayerAsync(Guid userId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Players.AsNoTracking().SingleAsync(p => p.UserId == userId);
    }

    [Fact]
    public async Task 跨一天_昨日達標_Streak為1()
    {
        var userId = await SeedUserAsync("UTC", lastSettled: Yesterday.AddDays(-1));
        await SeedLogAsync(userId, Yesterday, 0.8m);

        await SettleAsync(userId, Now);

        var player = await LoadPlayerAsync(userId);
        player.Streak.Should().Be(1);
        player.LastSettledDate.Should().Be(Yesterday);
        await using var db = fixture.CreateDbContext();
        var logs = await db.DailyLogs.Where(l => l.UserId == userId).OrderBy(l => l.Date).ToListAsync();
        logs.Select(l => l.Date).Should().Equal(Yesterday, Today);
        logs[0].IsSettled.Should().BeTrue();
        logs[0].IsCleared.Should().BeTrue();
        logs[1].IsSettled.Should().BeFalse();
    }

    [Fact]
    public async Task 跨一天_昨日未達標_Streak歸零()
    {
        var userId = await SeedUserAsync("UTC", lastSettled: Yesterday.AddDays(-1), streak: 6);
        await SeedLogAsync(userId, Yesterday, 0.5m);

        await SettleAsync(userId, Now);

        (await LoadPlayerAsync(userId)).Streak.Should().Be(0);
    }

    [Fact]
    public async Task 缺席5天_困難模式只扣3次懲罰()
    {
        var userId = await SeedUserAsync("UTC", lastSettled: Today.AddDays(-6), hardMode: true, streak: 9, level: 2, xp: 100);

        await SettleAsync(userId, Now);

        var player = await LoadPlayerAsync(userId);
        player.Streak.Should().Be(0);
        player.Level.Should().Be(2);
        player.Xp.Should().Be(100 - (3 * 18));
        await using var db = fixture.CreateDbContext();
        var penalties = await db.XpEvents.Where(e => e.UserId == userId && e.Source == XpSource.Penalty).ToListAsync();
        penalties.Should().HaveCount(3);
    }

    [Theory]
    [InlineData("2026-09-27T15:59:00Z", "2026-09-27")]
    [InlineData("2026-09-27T16:01:00Z", "2026-09-28")]
    public async Task 時區_台北使用者在UTC16點前後的今日不同(string utc, string expectedToday)
    {
        var now = DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture);
        var expected = DateOnly.Parse(expectedToday, System.Globalization.CultureInfo.InvariantCulture);
        var userId = await SeedUserAsync("Asia/Taipei", lastSettled: expected.AddDays(-1));

        await SettleAsync(userId, now);

        await using var db = fixture.CreateDbContext();
        var todayLog = await db.DailyLogs.SingleAsync(l => l.UserId == userId && !l.IsSettled);
        todayLog.Date.Should().Be(expected);
    }

    [Fact]
    public async Task 併發_兩個請求同時結算_Penalty只出現一次()
    {
        var userId = await SeedUserAsync("UTC", lastSettled: Today.AddDays(-2), hardMode: true, level: 1, xp: 50);

        await Task.WhenAll(SettleAsync(userId, Now), SettleAsync(userId, Now));

        await using var db = fixture.CreateDbContext();
        var penalties = await db.XpEvents.Where(e => e.UserId == userId && e.Source == XpSource.Penalty).ToListAsync();
        penalties.Should().ContainSingle();
        (await LoadPlayerAsync(userId)).Xp.Should().Be(50 - 15);
    }

    [Fact]
    public async Task 封存任務_不影響歷史DailyLog的CompletionRatio()
    {
        var userId = await SeedUserAsync("UTC", lastSettled: Yesterday.AddDays(-1));
        await SeedLogAsync(userId, Yesterday, 0.5m);
        await using (var db = fixture.CreateDbContext())
        {
            db.Quests.Add(new Quest { Id = Guid.NewGuid(), UserId = userId, Name = "q", QuestType = QuestType.Check, IsArchived = true, ArchivedAt = Now, CreatedAt = Now.AddDays(-5) });
            await db.SaveChangesAsync();
        }

        await SettleAsync(userId, Now);

        await using var verify = fixture.CreateDbContext();
        var log = await verify.DailyLogs.SingleAsync(l => l.UserId == userId && l.Date == Yesterday);
        log.CompletionRatio.Should().Be(0.5m);
        log.IsSettled.Should().BeTrue();
    }
}
