using FluentAssertions;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class RewardsTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static Player NewPlayer(int level = 1, int peakLevel = 1, int coins = 0, int shields = 0)
    {
        return new Player { UserId = UserId, Level = level, PeakLevel = peakLevel, Coins = coins, ShieldCount = shields };
    }

    private static DailyLog NewLog(bool bonusGranted = false, bool clearCoinsGranted = false)
    {
        return new DailyLog { Id = Guid.NewGuid(), UserId = UserId, BonusGranted = bonusGranted, ClearCoinsGranted = clearCoinsGranted };
    }

    private static RewardInput Input(
        Player player,
        int beforeLevel = 1,
        DailyLog? log = null,
        AchievementStats? stats = null,
        string[]? unlocked = null,
        int goals = 0,
        int programs = 0)
    {
        return new RewardInput(
            new PlayerSnapshot(beforeLevel),
            player,
            log ?? NewLog(),
            new HashSet<string>(unlocked ?? Array.Empty<string>()),
            stats ?? AchievementStats.Empty,
            goals,
            programs);
    }

    [Fact]
    public void Evaluate_升1級_發1個E箱並更新PeakLevel()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(level: 2), beforeLevel: 1));

        outcome.LevelsGained.Should().Be(1);
        outcome.NewPeakLevel.Should().Be(2);
        outcome.Chests.Should().Equal(new ChestGrant(Rarity.E, ChestSource.LevelUp));
        outcome.RankUps.Should().BeEmpty();
        outcome.ShieldsGained.Should().Be(0);
    }

    [Fact]
    public void Evaluate_一次跨2級_發2個E箱()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(level: 3), beforeLevel: 1));

        outcome.LevelsGained.Should().Be(2);
        outcome.Chests.Should().Equal(new ChestGrant(Rarity.E, ChestSource.LevelUp), new ChestGrant(Rarity.E, ChestSource.LevelUp));
    }

    [Fact]
    public void Evaluate_晉階E到D_另發C箱與1張保險卡()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(level: 5, peakLevel: 4), beforeLevel: 4));

        outcome.RankUps.Should().Equal("D");
        outcome.Chests.Should().Equal(new ChestGrant(Rarity.E, ChestSource.LevelUp), new ChestGrant(Rarity.C, ChestSource.RankUp));
        outcome.ShieldsGained.Should().Be(1);
    }

    [Fact]
    public void Evaluate_一次跨兩階_每階各發C箱與保險卡()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(level: 10, peakLevel: 4), beforeLevel: 4));

        outcome.RankUps.Should().Equal("D", "C");
        outcome.Chests.Count(c => c.Source == ChestSource.LevelUp).Should().Be(6);
        outcome.Chests.Count(c => c.Source == ChestSource.RankUp).Should().Be(2);
        outcome.ShieldsGained.Should().Be(2);
    }

    [Fact]
    public void Evaluate_晉階時已有3張保險卡_保險卡不超過上限但C箱照發()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(level: 5, peakLevel: 4, shields: 3), beforeLevel: 4));

        outcome.ShieldsGained.Should().Be(0);
        outcome.Chests.Should().Contain(new ChestGrant(Rarity.C, ChestSource.RankUp));
    }

    [Fact]
    public void Evaluate_撤銷後再升回曾到過的等級_不再發箱()
    {
        // 先前已到過 Lv2（PeakLevel 2），本次請求從 Lv1 升回 Lv2
        var outcome = Rewards.Evaluate(Input(NewPlayer(level: 2, peakLevel: 2), beforeLevel: 1));

        outcome.LevelsGained.Should().Be(1);
        outcome.NewPeakLevel.Should().Be(2);
        outcome.Chests.Should().BeEmpty();
        outcome.RankUps.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_無變化_結果為空()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer()));

        outcome.LevelsGained.Should().Be(0);
        outcome.NewPeakLevel.Should().Be(1);
        outcome.Chests.Should().BeEmpty();
        outcome.NewAchievements.Should().BeEmpty();
        outcome.Coins.Should().BeEmpty();
        outcome.ShieldsGained.Should().Be(0);
        outcome.ClearCoinsGranted.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_同請求撤銷再完成_等級與達標狀態不變_不發任何獎勵()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(), beforeLevel: 1, log: NewLog(bonusGranted: true, clearCoinsGranted: true)));

        outcome.Chests.Should().BeEmpty();
        outcome.Coins.Should().BeEmpty();
        outcome.ClearCoinsGranted.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_最佳連續7天_解鎖不屈並發C箱與50金幣()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(), stats: AchievementStats.Empty with { BestStreak = 7 }));

        outcome.NewAchievements.Select(a => a.Key).Should().Equal("streak-7");
        outcome.Chests.Should().Equal(new ChestGrant(Rarity.C, ChestSource.Streak7));
        outcome.Coins.Should().Equal(new CoinGrant(50, CoinSource.Achievement));
    }

    [Fact]
    public void Evaluate_不屈已解鎖_不再發C箱()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(), stats: AchievementStats.Empty with { BestStreak = 8 }, unlocked: ["streak-7"]));

        outcome.NewAchievements.Should().BeEmpty();
        outcome.Chests.Should().BeEmpty();
        outcome.Coins.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_最佳連續30天一次達成_解鎖不屈與恆心並發C箱與A箱()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(), stats: AchievementStats.Empty with { BestStreak = 30 }));

        outcome.NewAchievements.Select(a => a.Key).Should().Equal("streak-7", "streak-30");
        outcome.Chests.Should().Equal(new ChestGrant(Rarity.C, ChestSource.Streak7), new ChestGrant(Rarity.A, ChestSource.Streak30));
        outcome.Coins.Sum(c => c.Amount).Should().Be(100);
    }

    [Fact]
    public void Evaluate_完成2個目標_發2個A箱()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(), goals: 2));

        outcome.Chests.Should().Equal(new ChestGrant(Rarity.A, ChestSource.GoalCompleted), new ChestGrant(Rarity.A, ChestSource.GoalCompleted));
    }

    [Fact]
    public void Evaluate_完成66天週期_發S箱()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(), programs: 1));

        outcome.Chests.Should().Equal(new ChestGrant(Rarity.S, ChestSource.ProgramCompleted));
    }

    [Fact]
    public void Evaluate_分類連續30天_解鎖對應字塊並加50金幣()
    {
        var stats = AchievementStats.Empty with { RoleStreaks = new Dictionary<QuestRole, int> { [QuestRole.Bedtime] = 30 } };

        var outcome = Rewards.Evaluate(Input(NewPlayer(), stats: stats));

        outcome.NewAchievements.Select(a => a.Key).Should().Equal("bed-30");
        outcome.Coins.Should().Equal(new CoinGrant(50, CoinSource.Achievement));
        outcome.Chests.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_今日首次達標_加10金幣()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(), log: NewLog(bonusGranted: true)));

        outcome.Coins.Should().Equal(new CoinGrant(10, CoinSource.DailyClear));
        outcome.ClearCoinsGranted.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_撤銷達標_收回10金幣()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(coins: 30), log: NewLog(bonusGranted: false, clearCoinsGranted: true)));

        outcome.Coins.Should().Equal(new CoinGrant(-10, CoinSource.DailyClearUndo));
        outcome.ClearCoinsGranted.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_撤銷達標但餘額不足_只扣到0()
    {
        var partial = Rewards.Evaluate(Input(NewPlayer(coins: 4), log: NewLog(bonusGranted: false, clearCoinsGranted: true)));
        partial.Coins.Should().Equal(new CoinGrant(-4, CoinSource.DailyClearUndo));
        partial.ClearCoinsGranted.Should().BeFalse();

        var empty = Rewards.Evaluate(Input(NewPlayer(coins: 0), log: NewLog(bonusGranted: false, clearCoinsGranted: true)));
        empty.Coins.Should().BeEmpty();
        empty.ClearCoinsGranted.Should().BeFalse();
    }
}
