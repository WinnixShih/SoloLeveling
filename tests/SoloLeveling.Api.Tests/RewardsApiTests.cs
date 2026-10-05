using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class RewardsApiTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture.ConnectionString);

    public void Dispose()
    {
        _factory.Dispose();
    }

    private async Task<(HttpClient Client, Guid UserId)> RegisterAsync()
    {
        var client = await _factory.RegisterAsync();
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        return (client, me.GetProperty("user").GetProperty("id").GetGuid());
    }

    private async Task SeedAsync(Action<AppDbContext> seed)
    {
        await using var db = fixture.CreateDbContext();
        seed(db);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedChestAsync(Guid userId, Rarity rarity)
    {
        var id = Guid.NewGuid();
        await SeedAsync(db => db.RewardChests.Add(new RewardChest { Id = id, UserId = userId, Rarity = rarity, Source = ChestSource.Purchase, CreatedAt = _factory.Clock.GetUtcNow() }));
        return id;
    }

    /// <summary>測試直接種金幣餘額（不經 Wallet、不寫事件），只用來準備商店情境。</summary>
    private async Task SetCoinsAsync(Guid userId, int coins)
    {
        await using var db = fixture.CreateDbContext();
        var player = await db.Players.SingleAsync(p => p.UserId == userId);
        player.Coins = coins;
        await db.SaveChangesAsync();
    }

    private static Task<HttpResponseMessage> OpenAsync(HttpClient client, Guid chestId)
    {
        return client.PostAsync($"/api/v1/rewards/chests/{chestId}/open", null);
    }

    private static Task<HttpResponseMessage> BuyAsync(HttpClient client, string item, string? themeKey = null)
    {
        return client.PostAsJsonAsync("/api/v1/shop/purchase", new { item, themeKey });
    }

    private static async Task<JsonElement> OkJsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task ShouldFailAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be(code);
    }

    [Fact]
    public async Task 開箱_新卡加入收藏_再抽到同卡轉成重複金幣()
    {
        var (client, userId) = await RegisterAsync();
        var first = await SeedChestAsync(userId, Rarity.E);
        var second = await SeedChestAsync(userId, Rarity.E);
        _factory.Rng.Value = 0;

        var r1 = await OkJsonAsync(await OpenAsync(client, first));
        r1.GetProperty("card").GetProperty("id").GetString().Should().Be("rusty-dagger");
        r1.GetProperty("card").GetProperty("image").GetString().Should().Be("cards/rusty-dagger.webp");
        r1.GetProperty("isDuplicate").GetBoolean().Should().BeFalse();
        r1.GetProperty("coins").GetInt32().Should().Be(20);
        r1.GetProperty("coinBalance").GetInt32().Should().Be(20);

        var r2 = await OkJsonAsync(await OpenAsync(client, second));
        r2.GetProperty("isDuplicate").GetBoolean().Should().BeTrue();
        r2.GetProperty("coins").GetInt32().Should().Be(50);
        r2.GetProperty("coinBalance").GetInt32().Should().Be(70);

        var cards = await client.GetFromJsonAsync<JsonElement>("/api/v1/cards");
        cards.GetProperty("total").GetInt32().Should().Be(25);
        cards.GetProperty("ownedKinds").GetInt32().Should().Be(1);
        var dagger = cards.GetProperty("cards").EnumerateArray().Single(c => c.GetProperty("id").GetString() == "rusty-dagger");
        dagger.GetProperty("owned").GetBoolean().Should().BeTrue();
        dagger.GetProperty("count").GetInt32().Should().Be(2);
        dagger.GetProperty("firstAcquiredAt").GetInt64().Should().Be(_factory.Clock.GetUtcNow().ToUnixTimeSeconds());

        var rewards = await client.GetFromJsonAsync<JsonElement>("/api/v1/rewards");
        rewards.GetProperty("unopenedChests").GetArrayLength().Should().Be(0);
        rewards.GetProperty("coins").GetInt32().Should().Be(70);
        await using var db = fixture.CreateDbContext();
        var events = await db.CoinEvents.Where(e => e.UserId == userId).ToListAsync();
        events.Sum(e => e.Amount).Should().Be(70);
        events.Count(e => e.Source == CoinSource.ChestOpen).Should().Be(2);
        events.Should().ContainSingle(e => e.Source == CoinSource.DuplicateCard && e.Amount == 30 && e.RefId == second);
    }

    [Fact]
    public async Task 開已開過或別人的寶箱_回404()
    {
        var (client, userId) = await RegisterAsync();
        var other = await _factory.RegisterAsync();
        var chest = await SeedChestAsync(userId, Rarity.C);

        await ShouldFailAsync(await OpenAsync(other, chest), HttpStatusCode.NotFound, "ChestNotFound");
        await OkJsonAsync(await OpenAsync(client, chest));
        await ShouldFailAsync(await OpenAsync(client, chest), HttpStatusCode.NotFound, "ChestNotFound");
        await ShouldFailAsync(await OpenAsync(client, Guid.NewGuid()), HttpStatusCode.NotFound, "ChestNotFound");

        await using var db = fixture.CreateDbContext();
        (await db.OwnedCards.Where(c => c.UserId == userId).SumAsync(c => c.Count)).Should().Be(1);
        (await db.Players.SingleAsync(p => p.UserId == userId)).Coins.Should().Be(50);
    }

    [Fact]
    public async Task 開箱湊滿10種卡_解鎖收藏家()
    {
        var (client, userId) = await RegisterAsync();
        var now = _factory.Clock.GetUtcNow();
        await SeedAsync(db => db.OwnedCards.AddRange(Cards.OfRarity(Rarity.E).Skip(1).Take(9)
            .Select(c => new OwnedCard { UserId = userId, CardId = c.Id, Count = 1, FirstAcquiredAt = now })));
        var chest = await SeedChestAsync(userId, Rarity.E);
        _factory.Rng.Value = 0;

        var body = await OkJsonAsync(await OpenAsync(client, chest));

        body.GetProperty("isDuplicate").GetBoolean().Should().BeFalse();
        body.GetProperty("rewards").GetProperty("newAchievements").EnumerateArray()
            .Select(a => a.GetProperty("key").GetString()).Should().Contain("collector-10");
        body.GetProperty("coinBalance").GetInt32().Should().Be(70);
    }

    [Fact]
    public async Task 商店_扣款與各錯誤碼()
    {
        var (client, userId) = await RegisterAsync();
        await SetCoinsAsync(userId, 50);
        await ShouldFailAsync(await BuyAsync(client, "Shield"), HttpStatusCode.BadRequest, "NotEnoughCoins");

        await SetCoinsAsync(userId, 1000);
        JsonElement last = default;
        for (var i = 0; i < 3; i++)
        {
            last = await OkJsonAsync(await BuyAsync(client, "Shield"));
        }

        last.GetProperty("shieldCount").GetInt32().Should().Be(3);
        last.GetProperty("coins").GetInt32().Should().Be(700);
        await ShouldFailAsync(await BuyAsync(client, "Shield"), HttpStatusCode.BadRequest, "ShieldLimitReached");

        var violet = await OkJsonAsync(await BuyAsync(client, "Theme", "violet"));
        violet.GetProperty("coins").GetInt32().Should().Be(200);
        violet.GetProperty("ownedThemes").EnumerateArray().Select(t => t.GetString()).Should().Equal("azure", "violet");
        await ShouldFailAsync(await BuyAsync(client, "Theme", "violet"), HttpStatusCode.BadRequest, "ThemeOwned");
        await ShouldFailAsync(await BuyAsync(client, "Theme", "azure"), HttpStatusCode.BadRequest, "ThemeOwned");
        await ShouldFailAsync(await BuyAsync(client, "Theme", "neon"), HttpStatusCode.BadRequest, "UnknownTheme");

        var chest = await OkJsonAsync(await BuyAsync(client, "EChest"));
        chest.GetProperty("coins").GetInt32().Should().Be(0);
        chest.GetProperty("chest").GetProperty("rarity").GetString().Should().Be("E");
        chest.GetProperty("chest").GetProperty("source").GetString().Should().Be("Purchase");
        await ShouldFailAsync(await BuyAsync(client, "Theme", "jade"), HttpStatusCode.BadRequest, "NotEnoughCoins");

        var rewards = await client.GetFromJsonAsync<JsonElement>("/api/v1/rewards");
        rewards.GetProperty("unopenedChests").EnumerateArray().Select(c => c.GetProperty("id").GetGuid())
            .Should().Equal(chest.GetProperty("chest").GetProperty("id").GetGuid());
        await using var db = fixture.CreateDbContext();
        (await db.CoinEvents.Where(e => e.UserId == userId).SumAsync(e => e.Amount)).Should().Be(-1000);
        (await db.Players.SingleAsync(p => p.UserId == userId)).Coins.Should().Be(0);
    }

    [Fact]
    public async Task 稱號組合_只能選已解鎖且槽位正確的字塊()
    {
        var (client, userId) = await RegisterAsync();
        var now = _factory.Clock.GetUtcNow();
        await SeedAsync(db => db.Achievements.AddRange(
            new Achievement { UserId = userId, Key = "bed-30", UnlockedAt = now },
            new Achievement { UserId = userId, Key = "quests-100", UnlockedAt = now }));

        var both = await OkJsonAsync(await client.PutAsJsonAsync("/api/v1/me/title", new { prefixKey = "bed-30", suffixKey = "quests-100" }));
        both.GetProperty("player").GetProperty("title").GetString().Should().Be("靜夜的・百戰獵人");
        both.GetProperty("player").GetProperty("rankTitle").GetString().Should().Be("新手");

        await ShouldFailAsync(await client.PutAsJsonAsync("/api/v1/me/title", new { prefixKey = "quests-100", suffixKey = (string?)null }), HttpStatusCode.BadRequest, "TitleNotUnlocked");
        await ShouldFailAsync(await client.PutAsJsonAsync("/api/v1/me/title", new { prefixKey = (string?)null, suffixKey = "collector-10" }), HttpStatusCode.BadRequest, "TitleNotUnlocked");

        var prefixOnly = await OkJsonAsync(await client.PutAsJsonAsync("/api/v1/me/title", new { prefixKey = "bed-30", suffixKey = (string?)null }));
        prefixOnly.GetProperty("player").GetProperty("title").GetString().Should().Be("靜夜的");
        var none = await OkJsonAsync(await client.PutAsJsonAsync("/api/v1/me/title", new { prefixKey = (string?)null, suffixKey = (string?)null }));
        none.GetProperty("player").GetProperty("title").GetString().Should().Be("新手");

        var rewards = await client.GetFromJsonAsync<JsonElement>("/api/v1/rewards");
        rewards.GetProperty("titleFragments").EnumerateArray().Select(f => f.GetProperty("key").GetString()).Should().Equal("quests-100", "bed-30");
        rewards.GetProperty("title").GetString().Should().Be("新手");
    }

    [Fact]
    public async Task 主題切換_未擁有回400_擁有後可切換()
    {
        var (client, userId) = await RegisterAsync();

        await ShouldFailAsync(await client.PutAsJsonAsync("/api/v1/me/theme", new { themeKey = "violet" }), HttpStatusCode.BadRequest, "ThemeNotOwned");
        await SeedAsync(db => db.OwnedThemes.Add(new OwnedTheme { UserId = userId, ThemeKey = "violet" }));

        var violet = await OkJsonAsync(await client.PutAsJsonAsync("/api/v1/me/theme", new { themeKey = "violet" }));
        violet.GetProperty("player").GetProperty("themeKey").GetString().Should().Be("violet");
        var azure = await OkJsonAsync(await client.PutAsJsonAsync("/api/v1/me/theme", new { themeKey = "azure" }));
        azure.GetProperty("player").GetProperty("themeKey").GetString().Should().Be("azure");
        await ShouldFailAsync(await client.PutAsJsonAsync("/api/v1/me/theme", new { themeKey = "neon" }), HttpStatusCode.BadRequest, "ThemeNotOwned");
    }

    [Fact]
    public async Task 釘選卡片_未擁有回400_擁有後顯示在me_可取消()
    {
        var (client, userId) = await RegisterAsync();

        await ShouldFailAsync(await client.PutAsJsonAsync("/api/v1/me/pinned-card", new { cardId = "arise" }), HttpStatusCode.BadRequest, "CardNotOwned");
        await SeedAsync(db => db.OwnedCards.Add(new OwnedCard { UserId = userId, CardId = "arise", Count = 1, FirstAcquiredAt = _factory.Clock.GetUtcNow() }));

        var pinned = await OkJsonAsync(await client.PutAsJsonAsync("/api/v1/me/pinned-card", new { cardId = "arise" }));
        var card = pinned.GetProperty("player").GetProperty("pinnedCard");
        card.GetProperty("id").GetString().Should().Be("arise");
        card.GetProperty("rarity").GetString().Should().Be("S");

        var cleared = await OkJsonAsync(await client.PutAsJsonAsync("/api/v1/me/pinned-card", new { cardId = (string?)null }));
        cleared.GetProperty("player").GetProperty("pinnedCard").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetRewards_列出12個成就與進度_新帳號皆未解鎖()
    {
        var (client, _) = await RegisterAsync();
        var quests = await client.GetFromJsonAsync<JsonElement>("/api/v1/quests");
        await client.PutAsJsonAsync($"/api/v1/today/quests/{quests[0].GetProperty("id").GetGuid()}/progress", new { value = 1 });

        var rewards = await client.GetFromJsonAsync<JsonElement>("/api/v1/rewards");

        var achievements = rewards.GetProperty("achievements").EnumerateArray().ToList();
        achievements.Should().HaveCount(12);
        achievements.Should().OnlyContain(a => !a.GetProperty("unlocked").GetBoolean());
        var hundred = achievements.Single(a => a.GetProperty("key").GetString() == "quests-100");
        hundred.GetProperty("progress").GetInt32().Should().Be(1);
        hundred.GetProperty("target").GetInt32().Should().Be(100);
        hundred.GetProperty("unlockedAt").ValueKind.Should().Be(JsonValueKind.Null);
        rewards.GetProperty("coins").GetInt32().Should().Be(0);
        rewards.GetProperty("themeKey").GetString().Should().Be("azure");
        rewards.GetProperty("ownedThemes").EnumerateArray().Select(t => t.GetString()).Should().Equal("azure");
        rewards.GetProperty("title").GetString().Should().Be("新手");
        rewards.GetProperty("pinnedCard").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
