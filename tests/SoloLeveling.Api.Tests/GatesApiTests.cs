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

    private static async Task<HttpResponseMessage> OpenAsync(HttpClient client, object body)
    {
        return await client.PostAsJsonAsync("/api/v1/gates", body);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("error").GetProperty("code").GetString();
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

    [Fact]
    public async Task GetGates_新玩家_列出六級門且只有E級解鎖()
    {
        var (client, _, _) = await RegisterWithOneQuestAsync();

        var gates = await client.GetFromJsonAsync<JsonElement>("/api/v1/gates");

        var tiers = gates.GetProperty("tiers").EnumerateArray().ToList();
        tiers.Select(t => t.GetProperty("rank").GetString()).Should().Equal("E", "D", "C", "B", "A", "S");
        tiers.Select(t => t.GetProperty("unlocked").GetBoolean()).Should().Equal(true, false, false, false, false, false);
        tiers.Should().OnlyContain(t => t.GetProperty("clearCount").GetInt32() == 0);
        tiers[0].GetProperty("requiredRank").ValueKind.Should().Be(JsonValueKind.Null);
        tiers[0].GetProperty("achievementName").GetString().Should().Be("破繭");
        tiers[1].GetProperty("requiredRank").GetString().Should().Be("E");
        tiers[5].GetProperty("lengthDays").GetInt32().Should().Be(100);
        tiers[5].GetProperty("allowedMisses").GetInt32().Should().Be(10);
        tiers[5].GetProperty("chests").EnumerateArray().Select(c => c.GetString()).Should().Equal("S", "S", "S");
        tiers[5].GetProperty("titleText").GetString().Should().Be("S 級獵人");
        gates.GetProperty("activeGate").ValueKind.Should().Be(JsonValueKind.Null);
        gates.TryGetProperty("rewards", out _).Should().BeTrue();
    }

    [Fact]
    public async Task OpenGate_E級_回第1天並出現在me與gates()
    {
        var (client, _, _) = await RegisterWithOneQuestAsync();

        var response = await OpenAsync(client, new { rank = "E" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("rank").GetString().Should().Be("E");
        body.GetProperty("startDate").GetString().Should().Be("2026-09-28");
        body.GetProperty("dayNumber").GetInt32().Should().Be(1);
        body.GetProperty("lengthDays").GetInt32().Should().Be(5);
        body.GetProperty("misses").GetInt32().Should().Be(0);
        body.GetProperty("allowedMisses").GetInt32().Should().Be(1);
        body.GetProperty("status").GetString().Should().Be("Active");
        body.TryGetProperty("rewards", out _).Should().BeTrue();
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        me.GetProperty("activeGate").GetProperty("rank").GetString().Should().Be("E");
        var gates = await client.GetFromJsonAsync<JsonElement>("/api/v1/gates");
        gates.GetProperty("activeGate").GetProperty("rank").GetString().Should().Be("E");
    }

    [Fact]
    public async Task OpenGate_未解鎖_回400GateLocked()
    {
        var (client, _, _) = await RegisterWithOneQuestAsync();

        var response = await OpenAsync(client, new { rank = "D" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(response)).Should().Be("GateLocked");
    }

    [Fact]
    public async Task OpenGate_已有進行中的門_回409GateInProgress()
    {
        var (client, _, _) = await RegisterWithOneQuestAsync();
        (await OpenAsync(client, new { rank = "E" })).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await OpenAsync(client, new { rank = "E" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(response)).Should().Be("GateInProgress");
    }

    [Fact]
    public async Task OpenGate_等級無效或缺漏_回400且不開門()
    {
        var (client, _, _) = await RegisterWithOneQuestAsync();

        var numeric = await OpenAsync(client, new { rank = 9 });
        var missing = await OpenAsync(client, new { });
        var unknown = await OpenAsync(client, new { rank = "Z" });

        numeric.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(numeric)).Should().Be("InvalidGateRank");
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(missing)).Should().Be("InvalidGateRank");
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(unknown)).Should().Be("ValidationFailed");
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        me.GetProperty("activeGate").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task OpenGate_台北使用者跨日_開門日為使用者時區的今日()
    {
        var (client, _, _) = await RegisterWithOneQuestAsync("Asia/Taipei");
        // 10:00 UTC 起撥 7 小時 → 17:00 UTC，台北已是 2026-09-29 01:00
        _factory.Clock.Advance(TimeSpan.FromHours(7));

        var response = await OpenAsync(client, new { rank = "E" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("startDate").GetString().Should().Be("2026-09-29");
        body.GetProperty("dayNumber").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task OpenGate_通關低一級後_依序解鎖到S級()
    {
        var (client, _, userId) = await RegisterWithOneQuestAsync();
        foreach (var rank in new[] { GateRank.E, GateRank.D, GateRank.C, GateRank.B, GateRank.A })
        {
            await SeedGateAsync(userId, rank, Day1, GateStatus.Cleared);
        }

        var gates = await client.GetFromJsonAsync<JsonElement>("/api/v1/gates");

        var tiers = gates.GetProperty("tiers").EnumerateArray().ToList();
        tiers.Should().OnlyContain(t => t.GetProperty("unlocked").GetBoolean());
        tiers.Select(t => t.GetProperty("clearCount").GetInt32()).Should().Equal(1, 1, 1, 1, 1, 0);
        var response = await OpenAsync(client, new { rank = "S" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("lengthDays").GetInt32().Should().Be(100);
        body.GetProperty("allowedMisses").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task OpenGate_前一道門最後一天已結算_同一請求先補通關再開下一級()
    {
        var (client, questId, _) = await RegisterWithOneQuestAsync();
        (await OpenAsync(client, new { rank = "E" })).StatusCode.Should().Be(HttpStatusCode.OK);
        await ClearDaysAsync(client, questId, 4);
        await _factory.AdvanceAsync(client, TimeSpan.FromDays(1));

        var response = await OpenAsync(client, new { rank = "D" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("rank").GetString().Should().Be("D");
        body.GetProperty("startDate").GetString().Should().Be("2026-10-03");
        body.GetProperty("dayNumber").GetInt32().Should().Be(1);
        Rewards(body).GetProperty("gateCleared").GetString().Should().Be("E");
        Chests(body).Should().Contain("E:Gate");
        AchievementKeys(body).Should().Contain("gate-e");
    }

    [Fact]
    public async Task Abandon_沒有進行中的門_回404NoActiveGate()
    {
        var (client, _, _) = await RegisterWithOneQuestAsync();

        var response = await client.PostAsync("/api/v1/gates/active/abandon", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ErrorCodeAsync(response)).Should().Be("NoActiveGate");
    }

    [Fact]
    public async Task Abandon_放棄後可立刻重開且不算失敗也不發寶箱()
    {
        var (client, _, userId) = await RegisterWithOneQuestAsync();
        (await OpenAsync(client, new { rank = "E" })).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await client.PostAsync("/api/v1/gates/active/abandon", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Rewards(body).GetProperty("gateFailed").ValueKind.Should().Be(JsonValueKind.Null);
        Rewards(body).GetProperty("gateCleared").ValueKind.Should().Be(JsonValueKind.Null);
        Chests(body).Should().NotContain(c => c.EndsWith(":Gate"));
        var stored = await LatestGateAsync(userId);
        stored.Status.Should().Be(GateStatus.Abandoned);
        stored.EndedAt.Should().NotBeNull();
        (await OpenAsync(client, new { rank = "E" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var gates = await client.GetFromJsonAsync<JsonElement>("/api/v1/gates");
        gates.GetProperty("tiers")[0].GetProperty("clearCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Abandon_門已在結算後失敗_回200帶失敗結果不改成放棄()
    {
        var (client, _, userId) = await RegisterWithOneQuestAsync();
        (await OpenAsync(client, new { rank = "E" })).StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.AdvanceAsync(client, TimeSpan.FromDays(2));

        var response = await client.PostAsync("/api/v1/gates/active/abandon", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Rewards(body).GetProperty("gateFailed").GetString().Should().Be("E");
        (await LatestGateAsync(userId)).Status.Should().Be(GateStatus.Failed);
    }
}
