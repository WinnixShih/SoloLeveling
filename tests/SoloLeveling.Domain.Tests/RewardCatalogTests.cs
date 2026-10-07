using System.Text.RegularExpressions;
using FluentAssertions;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class RewardCatalogTests
{
    [Fact]
    public void Cards_共25張_E10C8A5S2()
    {
        Cards.All.Should().HaveCount(25);
        Cards.OfRarity(Rarity.E).Should().HaveCount(10);
        Cards.OfRarity(Rarity.C).Should().HaveCount(8);
        Cards.OfRarity(Rarity.A).Should().HaveCount(5);
        Cards.OfRarity(Rarity.S).Should().HaveCount(2);
    }

    [Fact]
    public void Cards_Id唯一且為kebabcase_圖片路徑依Id()
    {
        Cards.All.Select(c => c.Id).Should().OnlyHaveUniqueItems();
        Cards.All.Should().OnlyContain(c => Regex.IsMatch(c.Id, "^[a-z0-9]+(-[a-z0-9]+)*$"));
        Cards.All.Should().OnlyContain(c => c.Image == $"cards/{c.Id}.webp");
        Cards.All.Should().OnlyContain(c => c.Name.Length > 0 && c.Flavor.Length > 0);
    }

    [Fact]
    public void Cards_Find_找得到既有卡_找不到回null()
    {
        Cards.Find("rusty-dagger")!.Rarity.Should().Be(Rarity.E);
        Cards.Find("no-such-card").Should().BeNull();
        Cards.OfRarity(Rarity.E)[0].Id.Should().Be("rusty-dagger");
    }

    [Fact]
    public void Achievements_共12個且鍵唯一()
    {
        Achievements.All.Should().HaveCount(12);
        Achievements.All.Select(a => a.Key).Should().OnlyHaveUniqueItems();
        Achievements.Find("no-such").Should().BeNull();
    }

    [Theory]
    [InlineData("first-goal", "初次覺醒", "覺醒的", TitleSlot.Prefix)]
    [InlineData("streak-7", "不屈", "不屈的", TitleSlot.Prefix)]
    [InlineData("streak-30", "恆心", "恆心的", TitleSlot.Prefix)]
    [InlineData("quests-100", "百戰", "百戰獵人", TitleSlot.Suffix)]
    [InlineData("wake-30", "晨曦", "晨曦騎士", TitleSlot.Suffix)]
    [InlineData("bed-30", "靜夜", "靜夜的", TitleSlot.Prefix)]
    [InlineData("screen-30", "手機克星", "手機克星", TitleSlot.Suffix)]
    [InlineData("exercise-1000", "百里", "百里行者", TitleSlot.Suffix)]
    [InlineData("reading-1000", "藏書", "藏書者", TitleSlot.Suffix)]
    [InlineData("program-complete", "破繭", "破繭的", TitleSlot.Prefix)]
    [InlineData("collector-10", "收藏家", "收藏家", TitleSlot.Suffix)]
    [InlineData("rank-s", "傳說", "傳說的", TitleSlot.Prefix)]
    public void Achievements_第一批的名稱字塊與槽位(string key, string name, string text, TitleSlot slot)
    {
        var achievement = Achievements.Find(key)!;
        achievement.Name.Should().Be(name);
        achievement.TitleText.Should().Be(text);
        achievement.Slot.Should().Be(slot);
    }

    [Theory]
    [InlineData("first-goal", 1)]
    [InlineData("streak-7", 7)]
    [InlineData("streak-30", 30)]
    [InlineData("quests-100", 100)]
    [InlineData("wake-30", 30)]
    [InlineData("bed-30", 30)]
    [InlineData("screen-30", 30)]
    [InlineData("exercise-1000", 1000)]
    [InlineData("reading-1000", 1000)]
    [InlineData("program-complete", 1)]
    [InlineData("collector-10", 10)]
    [InlineData("rank-s", 45)]
    public void Achievements_達到門檻才解鎖(string key, int target)
    {
        var achievement = Achievements.Find(key)!;
        achievement.Target.Should().Be(target);
        achievement.IsMet(StatsFor(key, target)).Should().BeTrue();
        achievement.IsMet(StatsFor(key, target - 1)).Should().BeFalse();
    }

    [Fact]
    public void Achievements_分類連續只看自己的角色()
    {
        Achievements.Find("bed-30")!.IsMet(StatsFor("wake-30", 30)).Should().BeFalse();
    }

    [Fact]
    public void Achievements_進度不超過門檻()
    {
        Achievements.Find("quests-100")!.ProgressOf(StatsFor("quests-100", 250)).Should().Be(100);
        Achievements.Find("quests-100")!.ProgressOf(StatsFor("quests-100", 37)).Should().Be(37);
    }

    [Fact]
    public void Achievements_S階門檻與RankOf一致()
    {
        Leveling.RankOf(Achievements.SRankLevel).Rank.Should().Be("S");
        Leveling.RankOf(Achievements.SRankLevel - 1).Rank.Should().Be("A");
    }

    [Theory]
    [InlineData("bed-30", "quests-100", 1, "靜夜的・百戰獵人")]
    [InlineData("streak-7", null, 1, "不屈的")]
    [InlineData(null, "collector-10", 1, "收藏家")]
    [InlineData(null, null, 1, "新手")]
    [InlineData(null, null, 50, "傳說")]
    public void Titles_Compose_有選字塊就組合_都沒選沿用階級稱號(string? prefix, string? suffix, int level, string expected)
    {
        Titles.Compose(prefix, suffix, level).Should().Be(expected);
    }

    [Fact]
    public void Themes與Shop_常數符合規格()
    {
        Themes.Default.Should().Be("azure");
        Themes.All.Should().Equal("azure", "violet", "jade");
        Themes.Exists("violet").Should().BeTrue();
        Themes.Exists("neon").Should().BeFalse();
        Shop.ShieldPrice.Should().Be(100);
        Shop.ChestPrice.Should().Be(200);
        Shop.ThemePrice.Should().Be(500);
        Shop.MaxShields.Should().Be(3);
    }

    private static AchievementStats StatsFor(string key, int value)
    {
        var empty = AchievementStats.Empty;
        return key switch
        {
            "first-goal" => empty with { GoalCount = value },
            "streak-7" or "streak-30" => empty with { BestStreak = value },
            "quests-100" => empty with { TotalCompleted = value },
            "wake-30" => empty with { RoleStreaks = new Dictionary<QuestRole, int> { [QuestRole.WakeUp] = value } },
            "bed-30" => empty with { RoleStreaks = new Dictionary<QuestRole, int> { [QuestRole.Bedtime] = value } },
            "screen-30" => empty with { RoleStreaks = new Dictionary<QuestRole, int> { [QuestRole.ScreenTime] = value } },
            "exercise-1000" => empty with { RoleMinutes = new Dictionary<QuestRole, int> { [QuestRole.Exercise] = value } },
            "reading-1000" => empty with { RoleMinutes = new Dictionary<QuestRole, int> { [QuestRole.Reading] = value } },
            "program-complete" => empty with { CompletedPrograms = value },
            "collector-10" => empty with { OwnedCardKinds = value },
            "rank-s" => empty with { PeakLevel = value },
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "測試未涵蓋的成就"),
        };
    }
}
