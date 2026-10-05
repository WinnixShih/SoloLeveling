using FluentAssertions;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class WalletTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static Player NewPlayer(int coins)
    {
        return new Player { UserId = Guid.NewGuid(), Coins = coins };
    }

    [Fact]
    public void Change_加金幣_餘額增加並回傳對應事件()
    {
        var player = NewPlayer(5);
        var refId = Guid.NewGuid();

        var coinEvent = Wallet.Change(player, 20, CoinSource.ChestOpen, refId, Now);

        player.Coins.Should().Be(25);
        coinEvent.Id.Should().NotBe(Guid.Empty);
        coinEvent.UserId.Should().Be(player.UserId);
        coinEvent.Amount.Should().Be(20);
        coinEvent.Source.Should().Be(CoinSource.ChestOpen);
        coinEvent.RefId.Should().Be(refId);
        coinEvent.OccurredAt.Should().Be(Now);
    }

    [Fact]
    public void Change_扣到負數_丟InvalidOperationException且餘額不變()
    {
        var player = NewPlayer(5);

        var act = () => Wallet.Change(player, -6, CoinSource.DailyClearUndo, null, Now);

        act.Should().Throw<InvalidOperationException>();
        player.Coins.Should().Be(5);
    }

    [Fact]
    public void Spend_餘額不足_丟NotEnoughCoins且餘額不變()
    {
        var player = NewPlayer(99);

        var act = () => Wallet.Spend(player, Shop.ShieldPrice, CoinSource.ShopShield, null, Now);

        act.Should().Throw<DomainValidationException>().Which.Code.Should().Be("NotEnoughCoins");
        player.Coins.Should().Be(99);
    }

    [Fact]
    public void Spend_餘額足夠_扣款並回傳負數事件()
    {
        var player = NewPlayer(250);

        var coinEvent = Wallet.Spend(player, Shop.ChestPrice, CoinSource.ShopChest, null, Now);

        player.Coins.Should().Be(50);
        coinEvent.Amount.Should().Be(-200);
        coinEvent.Source.Should().Be(CoinSource.ShopChest);
    }
}
