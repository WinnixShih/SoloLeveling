using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SoloLeveling.Api.Services;
using SoloLeveling.Domain.Entities;

namespace SoloLeveling.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SettlementSchedulerTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture.ConnectionString);

    private static readonly DateOnly Today = new(2026, 9, 28);

    public void Dispose()
    {
        _factory.Dispose();
    }

    private async Task<Guid> SeedUserAsync(DateOnly? lastSettled)
    {
        await using var db = fixture.CreateDbContext();
        var now = _factory.Clock.GetUtcNow();
        var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@test.local", PasswordHash = "x", DisplayName = "t", TimeZoneId = "UTC", CreatedAt = now.AddDays(-30) };
        db.Users.Add(user);
        db.Players.Add(new Player { UserId = user.Id, LastSettledDate = lastSettled, CreatedAt = user.CreatedAt });
        await db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task RunOnce_只結算落後的使用者()
    {
        var behind = await SeedUserAsync(Today.AddDays(-5));
        var never = await SeedUserAsync(null);
        var current = await SeedUserAsync(Today.AddDays(-1));
        var scheduler = new SettlementScheduler(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.Clock,
            NullLogger<SettlementScheduler>.Instance);

        await scheduler.RunOnceAsync(CancellationToken.None);

        await using var db = fixture.CreateDbContext();
        (await db.Players.SingleAsync(p => p.UserId == behind)).LastSettledDate.Should().Be(Today.AddDays(-1));
        (await db.Players.SingleAsync(p => p.UserId == never)).LastSettledDate.Should().Be(Today.AddDays(-1));
        (await db.DailyLogs.CountAsync(l => l.UserId == behind)).Should().Be(5);
        // 已是最新的使用者不會被碰：連今日紀錄都不會被建立
        (await db.DailyLogs.CountAsync(l => l.UserId == current)).Should().Be(0);
    }
}
