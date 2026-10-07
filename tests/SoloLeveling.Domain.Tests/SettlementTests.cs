using FluentAssertions;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class SettlementTests
{
    private const string Tz = "UTC";
    private static readonly Guid UserId = Guid.NewGuid();

    // 2026-09-28 10:00 UTC → 今日 = 2026-09-28
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 28);
    private static readonly DateOnly Yesterday = new(2026, 9, 27);

    private static Player NewPlayer(DateOnly? lastSettled, bool hardMode = false)
    {
        return new Player
        {
            UserId = UserId,
            HardMode = hardMode,
            LastSettledDate = lastSettled,
            CreatedAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        };
    }

    private static DailyLog Log(DateOnly date, decimal ratio, bool settled = false)
    {
        return new DailyLog { Id = Guid.NewGuid(), UserId = UserId, Date = date, CompletionRatio = ratio, IsSettled = settled };
    }

    [Fact]
    public void Settle_昨日達標_Streak加1並標記已結算()
    {
        var player = NewPlayer(lastSettled: Yesterday.AddDays(-1));
        var yesterdayLog = Log(Yesterday, 0.8m);

        var result = Settlement.Settle(player, Tz, [yesterdayLog], Now);

        player.Streak.Should().Be(1);
        player.BestStreak.Should().Be(1);
        player.LastSettledDate.Should().Be(Yesterday);
        yesterdayLog.IsSettled.Should().BeTrue();
        yesterdayLog.IsCleared.Should().BeTrue();
        yesterdayLog.SettledAt.Should().Be(Now);
        result.Events.Should().BeEmpty();
    }

    [Fact]
    public void Settle_昨日未達標_Streak歸零()
    {
        var player = NewPlayer(lastSettled: Yesterday.AddDays(-1));
        player.Streak = 5;
        player.BestStreak = 5;

        Settlement.Settle(player, Tz, [Log(Yesterday, 0.5m)], Now);

        player.Streak.Should().Be(0);
        player.BestStreak.Should().Be(5);
    }

    [Fact]
    public void Settle_連續達標_Streak累加()
    {
        var player = NewPlayer(lastSettled: Yesterday.AddDays(-3));
        player.Streak = 2;
        var logs = new[] { Log(Yesterday.AddDays(-2), 1m), Log(Yesterday.AddDays(-1), 0.7m), Log(Yesterday, 0.9m) };

        Settlement.Settle(player, Tz, logs, Now);

        player.Streak.Should().Be(5);
        player.BestStreak.Should().Be(5);
    }

    [Fact]
    public void Settle_確保今日紀錄存在且未結算()
    {
        var player = NewPlayer(lastSettled: Yesterday);

        var result = Settlement.Settle(player, Tz, [], Now);

        result.Today.Should().Be(Today);
        result.TodayLog.Date.Should().Be(Today);
        result.TodayLog.IsSettled.Should().BeFalse();
        result.NewLogs.Should().ContainSingle(l => l.Date == Today);
    }

    [Fact]
    public void Settle_今日紀錄已存在_不重複建立()
    {
        var player = NewPlayer(lastSettled: Yesterday);
        var todayLog = Log(Today, 0.3m);

        var result = Settlement.Settle(player, Tz, [todayLog], Now);

        result.TodayLog.Should().BeSameAs(todayLog);
        result.NewLogs.Should().BeEmpty();
    }

    [Fact]
    public void Settle_缺席5天_補建紀錄且Streak歸零()
    {
        var player = NewPlayer(lastSettled: Today.AddDays(-6));
        player.Streak = 10;

        var result = Settlement.Settle(player, Tz, [], Now);

        result.NewLogs.Where(l => l.Date < Today).Should().HaveCount(5);
        result.NewLogs.Where(l => l.Date < Today).Should().OnlyContain(l => l.IsSettled && !l.IsCleared && l.CompletionRatio == 0);
        player.Streak.Should().Be(0);
        player.LastSettledDate.Should().Be(Yesterday);
    }

    [Fact]
    public void Settle_困難模式缺席5天_只懲罰3次且不降級()
    {
        var player = NewPlayer(lastSettled: Today.AddDays(-6), hardMode: true);
        player.Level = 2;
        player.Xp = 40;

        var result = Settlement.Settle(player, Tz, [], Now);

        var penalties = result.Events.Where(e => e.Source == XpSource.Penalty).ToList();
        penalties.Should().HaveCount(3);
        penalties.Should().OnlyContain(e => e.Amount == -18 && e.RefId != null);
        player.Level.Should().Be(2);
        player.Xp.Should().Be(0);
    }

    [Fact]
    public void Settle_一般模式缺席_不懲罰()
    {
        var player = NewPlayer(lastSettled: Today.AddDays(-3), hardMode: false);
        player.Xp = 40;

        var result = Settlement.Settle(player, Tz, [], Now);

        result.Events.Should().BeEmpty();
        player.Xp.Should().Be(40);
    }

    [Fact]
    public void Settle_從未結算過_從建立日起算()
    {
        var player = NewPlayer(lastSettled: null);
        player.CreatedAt = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

        var result = Settlement.Settle(player, Tz, [], Now);

        result.NewLogs.Select(l => l.Date).Should().BeEquivalentTo(
        [
            new DateOnly(2026, 9, 25), new DateOnly(2026, 9, 26), new DateOnly(2026, 9, 27), Today,
        ]);
        player.LastSettledDate.Should().Be(Yesterday);
    }

    [Fact]
    public void Settle_已結算的日子不重算()
    {
        var player = NewPlayer(lastSettled: Yesterday.AddDays(-2));
        var settledLog = Log(Yesterday.AddDays(-1), 1m, settled: true);
        settledLog.IsCleared = true;

        Settlement.Settle(player, Tz, [settledLog, Log(Yesterday, 1m)], Now);

        // 只有昨日被結算：Streak 從 0 → 1，不會因為已結算那天再 +1
        player.Streak.Should().Be(1);
    }

    [Fact]
    public void Settle_LastSettledDate不早於昨日_不動且不倒退()
    {
        var player = NewPlayer(lastSettled: Today);
        player.Streak = 4;

        var result = Settlement.Settle(player, Tz, [Log(Today, 0m)], Now);

        player.LastSettledDate.Should().Be(Today);
        player.Streak.Should().Be(4);
        result.NewLogs.Should().BeEmpty();
        result.Events.Should().BeEmpty();
    }

    [Fact]
    public void Settle_缺席超過400天_只逐日結算最近400天並歸零Streak()
    {
        var player = NewPlayer(lastSettled: Today.AddDays(-1000), hardMode: true);
        player.Streak = 7;

        var result = Settlement.Settle(player, Tz, [], Now);

        result.NewLogs.Where(l => l.Date < Today).Should().HaveCount(400);
        result.NewLogs.Min(l => l.Date).Should().Be(Today.AddDays(-400));
        player.Streak.Should().Be(0);
        player.LastSettledDate.Should().Be(Yesterday);
        result.Events.Where(e => e.Source == XpSource.Penalty).Should().HaveCount(3);
    }

    [Fact]
    public void Settle_依使用者時區決定今日()
    {
        // UTC 2026-09-27 16:30 → 台北 2026-09-28 00:30，今日 = 09-28，昨日 09-27 要被結算
        var now = new DateTimeOffset(2026, 9, 27, 16, 30, 0, TimeSpan.Zero);
        var player = NewPlayer(lastSettled: new DateOnly(2026, 9, 26));

        var result = Settlement.Settle(player, "Asia/Taipei", [Log(new DateOnly(2026, 9, 27), 1m)], now);

        result.Today.Should().Be(new DateOnly(2026, 9, 28));
        player.Streak.Should().Be(1);
    }

    [Fact]
    public void Settle_漏一天且有保險卡_消耗1張且連勝延續()
    {
        var player = NewPlayer(lastSettled: Yesterday.AddDays(-1));
        player.Streak = 4;
        player.BestStreak = 4;
        player.ShieldCount = 2;

        var result = Settlement.Settle(player, Tz, [Log(Yesterday, 0.2m)], Now);

        player.ShieldCount.Should().Be(1);
        player.Streak.Should().Be(5);
        player.BestStreak.Should().Be(5);
        result.ShieldsUsed.Should().Equal(Yesterday);
    }

    [Fact]
    public void Settle_補多天_保險卡逐日消耗_用完後歸零()
    {
        // 待結算 Yesterday-3 到 Yesterday 共 4 天，全部缺席
        var player = NewPlayer(lastSettled: Yesterday.AddDays(-4));
        player.Streak = 3;
        player.ShieldCount = 2;

        var result = Settlement.Settle(player, Tz, [], Now);

        result.ShieldsUsed.Should().Equal(Yesterday.AddDays(-3), Yesterday.AddDays(-2));
        player.ShieldCount.Should().Be(0);
        player.Streak.Should().Be(0);
        player.BestStreak.Should().Be(5);
    }

    [Fact]
    public void Settle_Streak為0時未達標_不消耗保險卡()
    {
        var player = NewPlayer(lastSettled: Yesterday.AddDays(-1));
        player.ShieldCount = 1;

        var result = Settlement.Settle(player, Tz, [Log(Yesterday, 0m)], Now);

        player.ShieldCount.Should().Be(1);
        player.Streak.Should().Be(0);
        result.ShieldsUsed.Should().BeEmpty();
    }

    [Fact]
    public void Settle_達標日_不消耗保險卡()
    {
        var player = NewPlayer(lastSettled: Yesterday.AddDays(-1));
        player.Streak = 2;
        player.ShieldCount = 1;

        var result = Settlement.Settle(player, Tz, [Log(Yesterday, 0.8m)], Now);

        player.ShieldCount.Should().Be(1);
        player.Streak.Should().Be(3);
        result.ShieldsUsed.Should().BeEmpty();
    }

    [Fact]
    public void Settle_困難模式用保險卡_連勝延續但懲罰照扣()
    {
        var player = NewPlayer(lastSettled: Yesterday.AddDays(-1), hardMode: true);
        player.Streak = 2;
        player.ShieldCount = 1;
        player.Xp = 50;

        // 0.9 < 困難模式門檻 1.0
        var result = Settlement.Settle(player, Tz, [Log(Yesterday, 0.9m)], Now);

        player.Streak.Should().Be(3);
        player.ShieldCount.Should().Be(0);
        player.Xp.Should().Be(35);
        result.Events.Should().ContainSingle(e => e.Source == XpSource.Penalty && e.Amount == -15);
        result.ShieldsUsed.Should().Equal(Yesterday);
    }

    [Fact]
    public void Settle_已全部結算過_SettledDays為0()
    {
        var player = NewPlayer(lastSettled: Yesterday);

        var result = Settlement.Settle(player, Tz, [], Now);

        result.SettledDays.Should().Be(0);
    }

    [Fact]
    public void Settle_缺席多日補結算_SettledDays等於補結算的日數()
    {
        var player = NewPlayer(lastSettled: Today.AddDays(-4));

        var result = Settlement.Settle(player, Tz, [], Now);

        result.SettledDays.Should().Be(3);
    }

    [Fact]
    public void Settle_缺席超過補結算上限_SettledDays為上限天數()
    {
        var player = NewPlayer(lastSettled: Today.AddDays(-(Settlement.MaxCatchUpDays + 50)));

        var result = Settlement.Settle(player, Tz, [], Now);

        result.SettledDays.Should().Be(Settlement.MaxCatchUpDays);
    }
}
