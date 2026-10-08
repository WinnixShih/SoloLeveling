using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Entities;

namespace SoloLeveling.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class GatesApiTests(PostgresFixture fixture) : IDisposable
{
    private static readonly DateOnly Day1 = new(2026, 9, 28);

    private readonly ApiFactory _factory = new(fixture.ConnectionString);

    public void Dispose()
    {
        _factory.Dispose();
    }

    /// <summary>註冊並只建一個勾選任務：勾完即 100% 達標，一天一個請求就能控制當天是否達標。</summary>
    private async Task<(HttpClient Client, Guid QuestId, Guid UserId)> RegisterWithOneQuestAsync(string timeZoneId = "UTC")
    {
        var client = await _factory.RegisterAsync(timeZoneId, seedBasicQuests: false);
        var response = await client.PostAsJsonAsync("/api/v1/quests", new { name = "伏地挺身", statType = "STR", difficulty = "Easy", questType = "Check" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var quest = await response.Content.ReadFromJsonAsync<JsonElement>();
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        return (client, quest.GetProperty("id").GetGuid(), me.GetProperty("user").GetProperty("id").GetGuid());
    }

    private async Task SeedGateAsync(Guid userId, GateRank rank, DateOnly startDate, GateStatus status = GateStatus.Active)
    {
        var definition = Gates.Of(rank);
        var now = _factory.Clock.GetUtcNow();
        await using var db = fixture.CreateDbContext();
        db.Gates.Add(new Gate
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Rank = rank,
            StartDate = startDate,
            LengthDays = definition.LengthDays,
            AllowedMisses = definition.AllowedMisses,
            Status = status,
            EndedAt = status == GateStatus.Active ? null : now,
            CreatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedShieldsAsync(Guid userId, int count)
    {
        await using var db = fixture.CreateDbContext();
        var player = await db.Players.SingleAsync(p => p.UserId == userId);
        player.ShieldCount = count;
        await db.SaveChangesAsync();
    }

    private async Task<Gate> LatestGateAsync(Guid userId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Gates.AsNoTracking().Where(g => g.UserId == userId).OrderByDescending(g => g.CreatedAt).FirstAsync();
    }

    private static async Task<JsonElement> SetDoneAsync(HttpClient client, Guid questId, int value)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/today/quests/{questId}/progress", new { value });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>連續 <paramref name="days"/> 天：當天勾完任務再撥到隔天。</summary>
    private async Task ClearDaysAsync(HttpClient client, Guid questId, int days)
    {
        for (var i = 0; i < days; i++)
        {
            await SetDoneAsync(client, questId, 1);
            await _factory.AdvanceAsync(client, TimeSpan.FromDays(1));
        }
    }

    private static JsonElement Rewards(JsonElement response)
    {
        return response.GetProperty("rewards");
    }

    private static List<string> Chests(JsonElement response)
    {
        return Rewards(response).GetProperty("newChests").EnumerateArray()
            .Select(c => $"{c.GetProperty("rarity").GetString()}:{c.GetProperty("source").GetString()}")
            .ToList();
    }

    private static List<string?> AchievementKeys(JsonElement response)
    {
        return Rewards(response).GetProperty("newAchievements").EnumerateArray().Select(a => a.GetProperty("key").GetString()).ToList();
    }

    [Fact]
    public async Task GetMe_新玩家_沒有進行中的門且獎勵帶傳送門欄位()
    {
        var (client, _, _) = await RegisterWithOneQuestAsync();

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");

        me.GetProperty("activeGate").ValueKind.Should().Be(JsonValueKind.Null);
        Rewards(me).GetProperty("gateCleared").ValueKind.Should().Be(JsonValueKind.Null);
        Rewards(me).GetProperty("gateFailed").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ProgramRestart_已移除_回404()
    {
        var (client, _, _) = await RegisterWithOneQuestAsync();

        var response = await client.PostAsync("/api/v1/program/restart", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMe_進行中的門_帶第幾天與已漏天數()
    {
        var (client, _, userId) = await RegisterWithOneQuestAsync();
        await SeedGateAsync(userId, GateRank.E, Day1);
        await _factory.AdvanceAsync(client, TimeSpan.FromDays(1));

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");

        var gate = me.GetProperty("activeGate");
        gate.GetProperty("rank").GetString().Should().Be("E");
        gate.GetProperty("startDate").GetString().Should().Be("2026-09-28");
        gate.GetProperty("dayNumber").GetInt32().Should().Be(2);
        gate.GetProperty("lengthDays").GetInt32().Should().Be(5);
        gate.GetProperty("misses").GetInt32().Should().Be(1);
        gate.GetProperty("allowedMisses").GetInt32().Should().Be(1);
        gate.GetProperty("status").GetString().Should().Be("Active");
        gate.TryGetProperty("rewards", out _).Should().BeFalse();
    }

    [Fact]
    public async Task 漏的天數超過上限_立即失敗且不發寶箱()
    {
        var (client, _, userId) = await RegisterWithOneQuestAsync();
        await SeedGateAsync(userId, GateRank.E, Day1);
        await _factory.AdvanceAsync(client, TimeSpan.FromDays(2));

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");

        Rewards(me).GetProperty("gateFailed").GetString().Should().Be("E");
        Rewards(me).GetProperty("gateCleared").ValueKind.Should().Be(JsonValueKind.Null);
        Chests(me).Should().NotContain(c => c.EndsWith(":Gate"));
        me.GetProperty("activeGate").ValueKind.Should().Be(JsonValueKind.Null);
        var stored = await LatestGateAsync(userId);
        stored.Status.Should().Be(GateStatus.Failed);
        stored.EndedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task 保險卡保住的日子_連勝不中斷但仍算漏一天()
    {
        var (client, questId, userId) = await RegisterWithOneQuestAsync();
        await SeedGateAsync(userId, GateRank.E, Day1);
        await SetDoneAsync(client, questId, 1);
        await SeedShieldsAsync(userId, 1);
        await _factory.AdvanceAsync(client, TimeSpan.FromDays(2));

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");

        Rewards(me).GetProperty("shieldsUsed").EnumerateArray().Select(d => d.GetString()).Should().Equal("2026-09-29");
        me.GetProperty("player").GetProperty("displayStreak").GetInt32().Should().Be(2);
        me.GetProperty("player").GetProperty("shieldCount").GetInt32().Should().Be(0);
        me.GetProperty("activeGate").GetProperty("misses").GetInt32().Should().Be(1);
        me.GetProperty("activeGate").GetProperty("status").GetString().Should().Be("Active");
        Rewards(me).GetProperty("gateFailed").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task 最後一天達標當下通關_撤銷後仍維持通關且不重發()
    {
        var (client, questId, userId) = await RegisterWithOneQuestAsync();
        await SeedGateAsync(userId, GateRank.E, Day1);
        await ClearDaysAsync(client, questId, 4);

        var cleared = await SetDoneAsync(client, questId, 1);

        Rewards(cleared).GetProperty("gateCleared").GetString().Should().Be("E");
        Chests(cleared).Where(c => c.EndsWith(":Gate")).Should().Equal("E:Gate");
        AchievementKeys(cleared).Should().Contain("gate-e");

        var undone = await SetDoneAsync(client, questId, 0);

        Rewards(undone).GetProperty("gateCleared").ValueKind.Should().Be(JsonValueKind.Null);
        Rewards(undone).GetProperty("gateFailed").ValueKind.Should().Be(JsonValueKind.Null);
        Chests(undone).Should().NotContain("E:Gate");
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        me.GetProperty("activeGate").ValueKind.Should().Be(JsonValueKind.Null);
        (await LatestGateAsync(userId)).Status.Should().Be(GateStatus.Cleared);

        await _factory.AdvanceAsync(client, TimeSpan.FromDays(1));
        var nextDay = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");

        Rewards(nextDay).GetProperty("gateCleared").ValueKind.Should().Be(JsonValueKind.Null);
        Rewards(nextDay).GetProperty("gateFailed").ValueKind.Should().Be(JsonValueKind.Null);
        Chests(nextDay).Should().NotContain("E:Gate");
    }

    [Fact]
    public async Task 最後一天結算後未超過上限_下次讀取通關且只發一次()
    {
        var (client, questId, userId) = await RegisterWithOneQuestAsync();
        await SeedGateAsync(userId, GateRank.E, Day1);
        await ClearDaysAsync(client, questId, 4);
        await _factory.AdvanceAsync(client, TimeSpan.FromDays(1));

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");

        Rewards(me).GetProperty("gateCleared").GetString().Should().Be("E");
        Chests(me).Should().Contain("E:Gate");
        AchievementKeys(me).Should().Contain("gate-e");
        me.GetProperty("activeGate").ValueKind.Should().Be(JsonValueKind.Null);

        var again = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");

        Rewards(again).GetProperty("gateCleared").ValueKind.Should().Be(JsonValueKind.Null);
        Chests(again).Should().BeEmpty();
    }
}
