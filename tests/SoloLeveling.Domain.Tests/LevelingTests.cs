using FluentAssertions;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class LevelingTests
{
    [Theory]
    [InlineData(1, 100)]
    [InlineData(2, 120)]
    [InlineData(10, 280)]
    public void XpNeeded_依等級線性成長(int level, int expected)
    {
        Leveling.XpNeeded(level).Should().Be(expected);
    }

    [Fact]
    public void GainXp_Lv1得100_升到Lv2且Xp歸0()
    {
        var player = new Player { Level = 1, Xp = 0 };

        Leveling.GainXp(player, 100);

        player.Level.Should().Be(2);
        player.Xp.Should().Be(0);
    }

    [Fact]
    public void GainXp_Lv1一次得250_連升到Lv3且剩30()
    {
        var player = new Player { Level = 1, Xp = 0 };

        Leveling.GainXp(player, 250);

        player.Level.Should().Be(3);
        player.Xp.Should().Be(30);
    }

    [Fact]
    public void LoseXp_Xp不足時降級並補回上一級所需()
    {
        var player = new Player { Level = 2, Xp = 5 };

        Leveling.LoseXp(player, 20);

        player.Level.Should().Be(1);
        player.Xp.Should().Be(85);
    }

    [Fact]
    public void LoseXp_Lv1不低於0()
    {
        var player = new Player { Level = 1, Xp = 5 };

        Leveling.LoseXp(player, 20);

        player.Level.Should().Be(1);
        player.Xp.Should().Be(0);
    }

    [Fact]
    public void ApplyPenalty_不降級且最低為0()
    {
        var player = new Player { Level = 3, Xp = 10 };

        Leveling.ApplyPenalty(player, 25);

        player.Level.Should().Be(3);
        player.Xp.Should().Be(0);
    }

    [Theory]
    [InlineData(1, "E", "新手")]
    [InlineData(4, "E", "新手")]
    [InlineData(5, "D", "見習者")]
    [InlineData(10, "C", "挑戰者")]
    [InlineData(20, "B", "精英")]
    [InlineData(30, "A", "大師")]
    [InlineData(44, "A", "大師")]
    [InlineData(45, "S", "傳說")]
    [InlineData(99, "S", "傳說")]
    public void RankOf_依等級區間回傳階級與稱號(int level, string rank, string title)
    {
        var result = Leveling.RankOf(level);

        result.Rank.Should().Be(rank);
        result.Title.Should().Be(title);
    }
}
