using FluentAssertions;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class GatesTests
{
    private static readonly DateOnly Start = new(2026, 10, 1);

    private static Gate NewGate(GateRank rank = GateRank.E)
    {
        var definition = Gates.Of(rank);
        return new Gate
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Rank = rank,
            StartDate = Start,
            LengthDays = definition.LengthDays,
            AllowedMisses = definition.AllowedMisses,
        };
    }

    [Theory]
    [InlineData(GateRank.E, 5, 1)]
    [InlineData(GateRank.D, 10, 2)]
    [InlineData(GateRank.C, 20, 3)]
    [InlineData(GateRank.B, 35, 5)]
    [InlineData(GateRank.A, 60, 8)]
    [InlineData(GateRank.S, 100, 10)]
    public void Gates_天數與可漏天數符合等級表(GateRank rank, int lengthDays, int allowedMisses)
    {
        var gate = Gates.Of(rank);

        gate.LengthDays.Should().Be(lengthDays);
        gate.AllowedMisses.Should().Be(allowedMisses);
    }

    [Fact]
    public void Gates_六級依序且通關寶箱符合等級表()
    {
        Gates.All.Select(g => g.Rank).Should().Equal(GateRank.E, GateRank.D, GateRank.C, GateRank.B, GateRank.A, GateRank.S);
        Gates.Of(GateRank.E).Chests.Should().Equal(Rarity.E);
        Gates.Of(GateRank.D).Chests.Should().Equal(Rarity.C);
        Gates.Of(GateRank.C).Chests.Should().Equal(Rarity.A);
        Gates.Of(GateRank.B).Chests.Should().Equal(Rarity.A, Rarity.A);
        Gates.Of(GateRank.A).Chests.Should().Equal(Rarity.S, Rarity.A);
        Gates.Of(GateRank.S).Chests.Should().Equal(Rarity.S, Rarity.S, Rarity.S);
    }

    [Fact]
    public void Gates_S級寶箱只出現在A與S級門()
    {
        Gates.All.Where(g => g.Chests.Contains(Rarity.S)).Select(g => g.Rank).Should().Equal(GateRank.A, GateRank.S);
    }

    [Fact]
    public void Gates_首次通關成就鍵唯一且依等級命名()
    {
        Gates.All.Select(g => g.AchievementKey).Should().Equal("gate-e", "gate-d", "gate-c", "gate-b", "gate-a", "gate-s");
    }

    [Fact]
    public void Gates_逐級解鎖_需先通關低一級至少一次()
    {
        var none = new Dictionary<GateRank, int>();

        Gates.Of(GateRank.E).RequiredRank.Should().BeNull();
        Gates.Of(GateRank.S).RequiredRank.Should().Be(GateRank.A);
        Gates.IsUnlocked(GateRank.E, none).Should().BeTrue();
        Gates.IsUnlocked(GateRank.D, none).Should().BeFalse();
        Gates.IsUnlocked(GateRank.D, new Dictionary<GateRank, int> { [GateRank.E] = 1 }).Should().BeTrue();
        Gates.IsUnlocked(GateRank.C, new Dictionary<GateRank, int> { [GateRank.E] = 3 }).Should().BeFalse();
        Gates.IsUnlocked(GateRank.S, new Dictionary<GateRank, int> { [GateRank.A] = 1 }).Should().BeTrue();
    }

    [Fact]
    public void Gates_Of未定義的等級_丟例外()
    {
        var act = () => Gates.Of((GateRank)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void LastDay_E級門_開門日加4天()
    {
        GateRules.LastDay(NewGate()).Should().Be(Start.AddDays(4));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 3)]
    [InlineData(4, 5)]
    [InlineData(10, 5)]
    [InlineData(-1, 1)]
    public void DayNumber_開門日為第1天_並夾在1到天數之間(int offset, int expected)
    {
        GateRules.DayNumber(NewGate(), Start.AddDays(offset)).Should().Be(expected);
    }

    [Fact]
    public void Judge_未到最後一天且未超過上限_進行中()
    {
        GateRules.Judge(NewGate(), Start.AddDays(2), 1, false).Should().Be(GateVerdict.InProgress);
    }

    [Fact]
    public void Judge_漏的天數超過上限_立即失敗()
    {
        GateRules.Judge(NewGate(), Start.AddDays(2), 2, false).Should().Be(GateVerdict.Failed);
    }

    [Fact]
    public void Judge_最後一天今日已達標_當天通關()
    {
        GateRules.Judge(NewGate(), Start.AddDays(4), 1, true).Should().Be(GateVerdict.Cleared);
    }

    [Fact]
    public void Judge_最後一天今日未達標_仍進行中()
    {
        GateRules.Judge(NewGate(), Start.AddDays(4), 1, false).Should().Be(GateVerdict.InProgress);
    }

    [Fact]
    public void Judge_最後一天已結算且未超過上限_通關()
    {
        GateRules.Judge(NewGate(), Start.AddDays(5), 1, false).Should().Be(GateVerdict.Cleared);
    }

    [Fact]
    public void Judge_最後一天已結算但超過上限_失敗()
    {
        GateRules.Judge(NewGate(), Start.AddDays(5), 2, false).Should().Be(GateVerdict.Failed);
    }

    [Fact]
    public void Judge_最後一天達標但已超過上限_失敗()
    {
        GateRules.Judge(NewGate(), Start.AddDays(4), 2, true).Should().Be(GateVerdict.Failed);
    }
}
