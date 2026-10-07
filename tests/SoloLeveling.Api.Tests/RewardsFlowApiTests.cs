using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SoloLeveling.Api.Services;

namespace SoloLeveling.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class RewardsFlowApiTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture.ConnectionString);

    public void Dispose()
    {
        _factory.Dispose();
    }

    private static async Task<List<Guid>> QuestIdsAsync(HttpClient client)
    {
        var quests = await client.GetFromJsonAsync<JsonElement>("/api/v1/quests");
        return quests.EnumerateArray().Select(q => q.GetProperty("id").GetGuid()).ToList();
    }

    private static async Task<JsonElement> PutProgressAsync(HttpClient client, Guid questId, object? value)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/today/quests/{questId}/progress", new { value });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> UserIdAsync(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        return me.GetProperty("user").GetProperty("id").GetGuid();
    }

    private async Task SeedShieldsAsync(Guid userId, int count)
    {
        await using var db = fixture.CreateDbContext();
        var player = await db.Players.SingleAsync(p => p.UserId == userId);
        player.ShieldCount = count;
        await db.SaveChangesAsync();
    }

    /// <summary>勾 index 0、1、4、6、7 五個 Check，喝水 8 杯，最後螢幕時間 2.5 小時使今日 7/9 達標；回傳達標那一次的回應。</summary>
    private static async Task<JsonElement> CompleteSevenAsync(HttpClient client, List<Guid> ids)
    {
        foreach (var i in new[] { 0, 1, 4, 6, 7 })
        {
            await PutProgressAsync(client, ids[i], 1);
        }

        await PutProgressAsync(client, ids[2], 8);
        return await PutProgressAsync(client, ids[5], 2.5);
    }

    private static async Task<JsonElement> CreateGoalAsync(HttpClient client, object goal)
    {
        var response = await client.PostAsJsonAsync("/api/v1/goals", new { goals = new[] { goal }, basicQuestIndexes = Array.Empty<int>() });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<string?> AchievementKeys(JsonElement response)
    {
        return response.GetProperty("rewards").GetProperty("newAchievements").EnumerateArray().Select(a => a.GetProperty("key").GetString()).ToList();
    }

    private static List<string> Chests(JsonElement response)
    {
        return response.GetProperty("rewards").GetProperty("newChests").EnumerateArray()
            .Select(c => $"{c.GetProperty("rarity").GetString()}:{c.GetProperty("source").GetString()}")
            .ToList();
    }

    [Fact]
    public async Task 完成任務升到Lv2_回應含等級提升與E箱()
    {
        var client = await _factory.RegisterAsync();
        var ids = await QuestIdsAsync(client);

        await PutProgressAsync(client, ids[1], 1);
        await PutProgressAsync(client, ids[6], 1);
        // 三個 Hard 任務共 105 EXP，第三次跨過 Lv1 的 100
        var today = await PutProgressAsync(client, ids[5], 2.5);

        today.GetProperty("rewards").GetProperty("levelsGained").GetInt32().Should().Be(1);
        Chests(today).Should().Equal("E:LevelUp");
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        me.GetProperty("player").GetProperty("level").GetInt32().Should().Be(2);
        me.GetProperty("rewards").GetProperty("newChests").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task 升級後撤銷再完成_不重複發E箱()
    {
        var client = await _factory.RegisterAsync();
        var userId = await UserIdAsync(client);
        var ids = await QuestIdsAsync(client);
        await PutProgressAsync(client, ids[1], 1);
        await PutProgressAsync(client, ids[6], 1);
        Chests(await PutProgressAsync(client, ids[5], 2.5)).Should().Equal("E:LevelUp");

        // 螢幕時間 4 小時超過上限 → 撤銷 35 EXP，降回 Lv1
        var undo = await PutProgressAsync(client, ids[5], 4);
        Chests(undo).Should().BeEmpty();
        var redo = await PutProgressAsync(client, ids[5], 2.5);

        redo.GetProperty("rewards").GetProperty("levelsGained").GetInt32().Should().Be(1);
        Chests(redo).Should().BeEmpty();
        await using var db = fixture.CreateDbContext();
        (await db.RewardChests.CountAsync(c => c.UserId == userId)).Should().Be(1);
    }

    [Fact]
    public async Task 今日首次達標加10金幣_撤銷達標收回()
    {
        var client = await _factory.RegisterAsync();
        var ids = await QuestIdsAsync(client);

        var cleared = await CompleteSevenAsync(client, ids);
        cleared.GetProperty("isCleared").GetBoolean().Should().BeTrue();
        cleared.GetProperty("rewards").GetProperty("coinDelta").GetInt32().Should().Be(10);

        var undone = await PutProgressAsync(client, ids[5], 4);
        undone.GetProperty("isCleared").GetBoolean().Should().BeFalse();
        undone.GetProperty("rewards").GetProperty("coinDelta").GetInt32().Should().Be(-10);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        me.GetProperty("player").GetProperty("coins").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task 封存未完成任務使今日達標_回200並帶金幣_任務清單不帶rewards()
    {
        var client = await _factory.RegisterAsync();
        var ids = await QuestIdsAsync(client);
        foreach (var i in new[] { 0, 1, 4, 6, 7 })
        {
            await PutProgressAsync(client, ids[i], 1);
        }

        await PutProgressAsync(client, ids[2], 8);

        // 6/9 未達標；封存未完成的「三件好事」後 6/8 = 75%
        var response = await client.DeleteAsync($"/api/v1/quests/{ids[8]}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("rewards").GetProperty("coinDelta").GetInt32().Should().Be(10);
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/quests");
        list[0].TryGetProperty("rewards", out _).Should().BeFalse();
    }

    [Fact]
    public async Task 有保險卡時漏一天_連勝延續並在下次回應公告一次()
    {
        var client = await _factory.RegisterAsync();
        var userId = await UserIdAsync(client);
        await SeedShieldsAsync(userId, 1);
        await CompleteSevenAsync(client, await QuestIdsAsync(client));

        // 09-28 達標、09-29 漏掉、今天 09-30
        _factory.Clock.Advance(TimeSpan.FromDays(2));
        var today = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");

        var rewards = today.GetProperty("rewards");
        rewards.GetProperty("shieldsUsed").EnumerateArray().Select(d => d.GetString()).Should().Equal("2026-09-29");
        rewards.GetProperty("shieldCount").GetInt32().Should().Be(0);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        me.GetProperty("player").GetProperty("displayStreak").GetInt32().Should().Be(2);
        me.GetProperty("player").GetProperty("shieldCount").GetInt32().Should().Be(0);
        me.GetProperty("rewards").GetProperty("shieldsUsed").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task 排程結算消耗保險卡_下次請求公告一次()
    {
        var client = await _factory.RegisterAsync();
        var userId = await UserIdAsync(client);
        await SeedShieldsAsync(userId, 1);
        await CompleteSevenAsync(client, await QuestIdsAsync(client));
        _factory.Clock.Advance(TimeSpan.FromDays(2));
        var scheduler = new SettlementScheduler(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.Clock,
            NullLogger<SettlementScheduler>.Instance);

        await scheduler.RunOnceAsync(CancellationToken.None);

        await using (var db = fixture.CreateDbContext())
        {
            (await db.Players.SingleAsync(p => p.UserId == userId)).ShieldCount.Should().Be(0);
            (await db.RewardEvents.SingleAsync(e => e.UserId == userId)).AnnouncedAt.Should().BeNull();
        }

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        me.GetProperty("rewards").GetProperty("shieldsUsed").EnumerateArray().Select(d => d.GetString()).Should().Equal("2026-09-29");
        var again = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        again.GetProperty("rewards").GetProperty("shieldsUsed").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task 目標完成發A箱且只發一次_建立第一個目標解鎖初次覺醒_連續7天解鎖不屈()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);
        // 閱讀 20 → 30 分鐘、7 天：3 階、每階 3 天，第 7 個達標日在最後一階
        var created = await CreateGoalAsync(client, new { category = "Reading", answers = new { currentMinutes = 20, targetMinutes = 30, lengthDays = 7 } });
        AchievementKeys(created).Should().Equal("first-goal");
        var readingId = created.GetProperty("goals")[0].GetProperty("quests")[0].GetProperty("id").GetGuid();

        for (var day = 1; day <= 6; day++)
        {
            var progress = await PutProgressAsync(client, readingId, 30);
            Chests(progress).Should().NotContain("A:GoalCompleted");
            _factory.Clock.Advance(TimeSpan.FromDays(1));
        }

        var day7 = await PutProgressAsync(client, readingId, 30);
        Chests(day7).Should().Contain(new[] { "A:GoalCompleted", "C:Streak7" });
        AchievementKeys(day7).Should().Contain("streak-7");

        _factory.Clock.Advance(TimeSpan.FromDays(1));
        var day8 = await PutProgressAsync(client, readingId, 30);
        Chests(day8).Should().NotContain("A:GoalCompleted");
        await using var db = fixture.CreateDbContext();
        var goalId = (await db.Quests.SingleAsync(q => q.Id == readingId)).GoalId!.Value;
        (await db.Goals.SingleAsync(g => g.Id == goalId)).CompletedAt.Should().NotBeNull();
    }

    /// <summary>起訖相同的 7 天閱讀目標：第 1 天達標，期間要到第 7 天才走完；排程在第 7 天早上已結算完前 6 天。</summary>
    private async Task<(HttpClient Client, Guid GoalId)> SetupGoalFinishingTodayBySchedulerAsync()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);
        var created = await CreateGoalAsync(client, new { category = "Reading", answers = new { currentMinutes = 30, targetMinutes = 30, lengthDays = 7 } });
        var goalId = created.GetProperty("goals")[0].GetProperty("id").GetGuid();
        var readingId = created.GetProperty("goals")[0].GetProperty("quests")[0].GetProperty("id").GetGuid();
        await PutProgressAsync(client, readingId, 30);
        _factory.Clock.Advance(TimeSpan.FromDays(6));
        var scheduler = new SettlementScheduler(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.Clock,
            NullLogger<SettlementScheduler>.Instance);
        await scheduler.RunOnceAsync(CancellationToken.None);
        return (client, goalId);
    }

    [Fact]
    public async Task 排程已結算後的第一次讀取_補發當天走完期間的A箱_同日第二次讀取不再判定()
    {
        var (client, _) = await SetupGoalFinishingTodayBySchedulerAsync();

        var first = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");
        _factory.Sql.Clear();
        var second = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");

        Chests(first).Should().Contain("A:GoalCompleted");
        Chests(second).Should().BeEmpty();
        _factory.Sql.Count("FROM \"Achievements\"").Should().Be(0);
    }

    [Fact]
    public async Task 目標走完期間當天直接封存_仍發A箱()
    {
        var (client, goalId) = await SetupGoalFinishingTodayBySchedulerAsync();

        var response = await client.DeleteAsync($"/api/v1/goals/{goalId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Chests(await response.Content.ReadFromJsonAsync<JsonElement>()).Should().Contain("A:GoalCompleted");
    }

    [Fact]
    public async Task 起訖相同的目標第一天達標_期間未走完不發A箱()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);
        var created = await CreateGoalAsync(client, new { category = "Reading", answers = new { currentMinutes = 30, targetMinutes = 30, lengthDays = 7 } });
        var readingId = created.GetProperty("goals")[0].GetProperty("quests")[0].GetProperty("id").GetGuid();

        var progress = await PutProgressAsync(client, readingId, 30);

        Chests(progress).Should().NotContain("A:GoalCompleted");
        await using var db = fixture.CreateDbContext();
        var goalId = (await db.Quests.SingleAsync(q => q.Id == readingId)).GoalId!.Value;
        (await db.Goals.SingleAsync(g => g.Id == goalId)).CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task 開新週期_舊週期滿66天_發S箱並解鎖破繭()
    {
        var client = await _factory.RegisterAsync();
        await _factory.AdvanceAsync(client, TimeSpan.FromDays(66));

        var response = await client.PostAsync("/api/v1/program/restart", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("cycle").GetInt32().Should().Be(2);
        Chests(body).Should().Contain("S:ProgramCompleted");
        AchievementKeys(body).Should().Contain("program-complete");
    }

    [Fact]
    public async Task 開新週期_舊週期未滿66天_不發S箱()
    {
        var client = await _factory.RegisterAsync();
        await _factory.AdvanceAsync(client, TimeSpan.FromDays(65));

        var body = await (await client.PostAsync("/api/v1/program/restart", null)).Content.ReadFromJsonAsync<JsonElement>();

        Chests(body).Should().NotContain("S:ProgramCompleted");
        AchievementKeys(body).Should().NotContain("program-complete");
    }

    [Fact]
    public async Task 螢幕時間任務連續30天_第30天解鎖手機克星()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);
        var created = await CreateGoalAsync(client, new { category = "ScreenTime", answers = new { currentHours = 4, targetHours = 2, lengthDays = 30 } });
        var screenId = created.GetProperty("goals")[0].GetProperty("quests")[0].GetProperty("id").GetGuid();

        for (var day = 1; day <= 29; day++)
        {
            AchievementKeys(await PutProgressAsync(client, screenId, 0)).Should().NotContain("screen-30");
            await _factory.AdvanceAsync(client, TimeSpan.FromDays(1));
        }

        AchievementKeys(await PutProgressAsync(client, screenId, 0)).Should().Contain("screen-30");
    }

    [Fact]
    public async Task 閱讀任務累計1000分鐘_解鎖藏書()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);
        var created = await CreateGoalAsync(client, new { category = "Reading", answers = new { currentMinutes = 0, targetMinutes = 30, lengthDays = 30 } });
        var readingId = created.GetProperty("goals")[0].GetProperty("quests")[0].GetProperty("id").GetGuid();

        AchievementKeys(await PutProgressAsync(client, readingId, 999)).Should().NotContain("reading-1000");
        AchievementKeys(await PutProgressAsync(client, readingId, 1000)).Should().Contain("reading-1000");
    }
}
