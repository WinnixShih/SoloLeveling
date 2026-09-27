using FluentAssertions;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class UserClockTests
{
    [Theory]
    [InlineData("2026-09-27T15:59:00Z", "2026-09-27")]
    [InlineData("2026-09-27T16:01:00Z", "2026-09-28")]
    public void DateOf_台北時區在UTC16點前後為不同日(string utc, string expected)
    {
        var now = DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture);

        UserClock.DateOf(now, "Asia/Taipei").Should().Be(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void DateOf_UTC時區直接取日期()
    {
        var now = new DateTimeOffset(2026, 9, 27, 23, 30, 0, TimeSpan.Zero);

        UserClock.DateOf(now, "UTC").Should().Be(new DateOnly(2026, 9, 27));
    }
}
