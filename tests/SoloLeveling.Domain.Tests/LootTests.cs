using FluentAssertions;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class LootTests
{
    /// <summary>固定回傳指定索引（超過上限取最後一個），並記下最後一次的上限。</summary>
    private sealed class FixedRandom(int value) : Random
    {
        public int? LastMaxValue { get; private set; }

        public override int Next(int maxValue)
        {
            LastMaxValue = maxValue;
            return Math.Min(value, maxValue - 1);
        }
    }

    [Fact]
    public void Open_未擁有_成為新卡且只得開箱金幣()
    {
        var result = Loot.Open(Rarity.E, new Dictionary<string, int>(), new FixedRandom(0));

        result.Should().Be(new LootResult("rusty-dagger", false, 20, 0));
        result.Coins.Should().Be(20);
    }

    [Fact]
    public void Open_已擁有_重複卡另得重複金幣()
    {
        var result = Loot.Open(Rarity.E, new Dictionary<string, int> { ["rusty-dagger"] = 1 }, new FixedRandom(0));

        result.Should().Be(new LootResult("rusty-dagger", true, 20, 30));
        result.Coins.Should().Be(50);
    }

    [Theory]
    [InlineData(Rarity.E, 20, 30)]
    [InlineData(Rarity.C, 50, 80)]
    [InlineData(Rarity.A, 100, 200)]
    [InlineData(Rarity.S, 300, 500)]
    public void Open_各等級的開箱金幣與重複金幣(Rarity rarity, int baseCoins, int duplicateCoins)
    {
        var cardId = Cards.OfRarity(rarity)[1].Id;

        var result = Loot.Open(rarity, new Dictionary<string, int> { [cardId] = 2 }, new FixedRandom(1));

        result.Should().Be(new LootResult(cardId, true, baseCoins, duplicateCoins));
    }

    [Theory]
    [InlineData(Rarity.E, 10)]
    [InlineData(Rarity.C, 8)]
    [InlineData(Rarity.A, 5)]
    [InlineData(Rarity.S, 2)]
    public void Open_從該等級全部卡片均勻抽_含已擁有(Rarity rarity, int poolSize)
    {
        var rng = new FixedRandom(99);
        var owned = Cards.OfRarity(rarity).ToDictionary(c => c.Id, _ => 1);

        var result = Loot.Open(rarity, owned, rng);

        rng.LastMaxValue.Should().Be(poolSize);
        Cards.Find(result.CardId)!.Rarity.Should().Be(rarity);
        result.IsDuplicate.Should().BeTrue();
    }
}
