using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace SoloLeveling.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class TodayApiTests(PostgresFixture fixture) : IDisposable
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

    private static async Task<JsonElement> SetProgressAsync(HttpClient client, Guid questId, object? value)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/today/quests/{questId}/progress", new { value });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>預設 9 個任務中，index 0、1、4、6、7、8 是 Check；勾前 7 個 Check 以外還需要 Count／Limit，這裡直接勾 index 0,1,4,6,7 五個 Check 加上喝水（Count 8）與螢幕時間（Limit 3）。</summary>
    private static async Task CompleteSevenAsync(HttpClient client, List<Guid> ids)
    {
        foreach (var i in new[] { 0, 1, 4, 6, 7 })
        {
            await SetProgressAsync(client, ids[i], 1);
        }

        await SetProgressAsync(client, ids[2], 8);
        await SetProgressAsync(client, ids[5], 2.5);
    }

    [Fact]
    public async Task GetToday_當日無新結算_不跑獎勵統計但仍回rewards欄位()
    {
        var client = await _factory.RegisterAsync();
        (await client.GetAsync("/api/v1/today")).EnsureSuccessStatusCode();
        _factory.Sql.Clear();

        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");

        _factory.Sql.Count("FROM \"Achievements\"").Should().Be(0);
        body.GetProperty("rewards").GetProperty("newChests").GetArrayLength().Should().Be(0);
        me.GetProperty("rewards").GetProperty("shieldsUsed").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetToday_跨日結算後_仍跑完整獎勵判定()
    {
        var client = await _factory.RegisterAsync();
        _factory.Clock.Advance(TimeSpan.FromDays(1));
        _factory.Sql.Clear();

        var response = await client.GetAsync("/api/v1/today");

        response.EnsureSuccessStatusCode();
        _factory.Sql.Count("FROM \"Achievements\"").Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetToday_新玩家_9個任務皆未完成()
    {
        var client = await _factory.RegisterAsync();

        var today = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");

        today.GetProperty("date").GetString().Should().Be("2026-09-28");
        today.GetProperty("completionRatio").GetDecimal().Should().Be(0);
        today.GetProperty("isCleared").GetBoolean().Should().BeFalse();
        today.GetProperty("threshold").GetDecimal().Should().Be(0.7m);
        today.GetProperty("bonusGranted").GetBoolean().Should().BeFalse();
        today.GetProperty("note").ValueKind.Should().Be(JsonValueKind.Null);
        var quests = today.GetProperty("quests");
        quests.GetArrayLength().Should().Be(9);
        var first = quests[0];
        first.GetProperty("name").GetString().Should().Be("23:30 前上床睡覺");
        first.GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
        first.GetProperty("isDone").GetBoolean().Should().BeFalse();
        first.GetProperty("xpReward").GetInt32().Should().Be(20);
        first.GetProperty("statReward").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task PutProgress_勾選Check任務_回傳今日並加EXP()
    {
        var client = await _factory.RegisterAsync();
        var ids = await QuestIdsAsync(client);

        var today = await SetProgressAsync(client, ids[0], 1);

        var quest = today.GetProperty("quests").EnumerateArray().Single(q => q.GetProperty("id").GetGuid() == ids[0]);
        quest.GetProperty("value").GetDecimal().Should().Be(1);
        quest.GetProperty("isDone").GetBoolean().Should().BeTrue();
        today.GetProperty("completionRatio").GetDecimal().Should().Be(0.1111m);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        me.GetProperty("player").GetProperty("xp").GetInt32().Should().Be(20);
        me.GetProperty("player").GetProperty("stats").GetProperty("vit").GetInt32().Should().Be(11);
        me.GetProperty("player").GetProperty("totalCompleted").GetInt32().Should().Be(1);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, -1)]
    public async Task PutProgress_不合法的值_回400(int questIndex, double value)
    {
        var client = await _factory.RegisterAsync();
        var ids = await QuestIdsAsync(client);

        var response = await client.PutAsJsonAsync($"/api/v1/today/quests/{ids[questIndex]}/progress", new { value });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PutProgress_不存在的任務_回404()
    {
        var client = await _factory.RegisterAsync();

        var response = await client.PutAsJsonAsync($"/api/v1/today/quests/{Guid.NewGuid()}/progress", new { value = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task 完成7個任務_達標並發30EXP_事件新到舊且時間為Unix秒()
    {
        var client = await _factory.RegisterAsync();
        var ids = await QuestIdsAsync(client);

        await CompleteSevenAsync(client, ids);

        var today = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");
        today.GetProperty("completionRatio").GetDecimal().Should().Be(0.7778m);
        today.GetProperty("isCleared").GetBoolean().Should().BeTrue();
        today.GetProperty("bonusGranted").GetBoolean().Should().BeTrue();

        var events = await client.GetFromJsonAsync<JsonElement>("/api/v1/xp-events?limit=50");
        var list = events.EnumerateArray().ToList();
        list.Should().HaveCount(8);
        list.Count(e => e.GetProperty("source").GetString() == "DailyBonus").Should().Be(1);
        list.Count(e => e.GetProperty("source").GetString() == "Quest").Should().Be(7);
        // 同一請求內的事件時間相同，只驗證內容不驗證彼此先後
        var bonus = list.Single(e => e.GetProperty("source").GetString() == "DailyBonus");
        bonus.GetProperty("amount").GetInt32().Should().Be(30);
        var occurredAt = bonus.GetProperty("occurredAt").GetInt64();
        occurredAt.Should().Be(_factory.Clock.GetUtcNow().ToUnixTimeSeconds());
        occurredAt.ToString().Should().HaveLength(10);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        me.GetProperty("player").GetProperty("displayStreak").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task 達標後撤銷_收回獎勵()
    {
        var client = await _factory.RegisterAsync();
        var ids = await QuestIdsAsync(client);
        await CompleteSevenAsync(client, ids);

        var today = await SetProgressAsync(client, ids[0], 0);

        today.GetProperty("isCleared").GetBoolean().Should().BeFalse();
        today.GetProperty("bonusGranted").GetBoolean().Should().BeFalse();
        var events = await client.GetFromJsonAsync<JsonElement>("/api/v1/xp-events");
        // 最新兩筆是同一請求產生的撤銷事件，時間相同不驗證彼此先後
        events.EnumerateArray().Take(2).Select(e => e.GetProperty("source").GetString())
            .Should().BeEquivalentTo("DailyBonusUndo", "QuestUndo");
        events.EnumerateArray().Single(e => e.GetProperty("source").GetString() == "DailyBonusUndo")
            .GetProperty("amount").GetInt32().Should().Be(-30);
    }

    [Fact]
    public async Task PutNote_寫入後可在GetToday讀到()
    {
        var client = await _factory.RegisterAsync();

        var response = await client.PutAsJsonAsync("/api/v1/today/note", new { note = "今天很順" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var today = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");
        today.GetProperty("note").GetString().Should().Be("今天很順");
    }

    [Fact]
    public async Task PutNote_超過2000字_回400()
    {
        var client = await _factory.RegisterAsync();

        var response = await client.PutAsJsonAsync("/api/v1/today/note", new { note = new string('a', 2001) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetHistory_含今日即時值與doneQuestIds()
    {
        var client = await _factory.RegisterAsync();
        var ids = await QuestIdsAsync(client);
        await SetProgressAsync(client, ids[0], 1);

        var history = await client.GetFromJsonAsync<JsonElement>("/api/v1/history?from=2026-09-28&to=2026-09-28");

        history.GetArrayLength().Should().Be(1);
        var day = history[0];
        day.GetProperty("date").GetString().Should().Be("2026-09-28");
        day.GetProperty("completionRatio").GetDecimal().Should().Be(0.1111m);
        day.GetProperty("isCleared").GetBoolean().Should().BeFalse();
        day.GetProperty("doneQuestIds").EnumerateArray().Select(x => x.GetGuid()).Should().Equal(ids[0]);
    }

    [Theory]
    [InlineData("from=2026-01-01&to=2026-06-30")]
    [InlineData("from=2026-09-28&to=2026-09-27")]
    [InlineData("from=bad&to=2026-09-28")]
    public async Task GetHistory_區間不合法_回400(string query)
    {
        var client = await _factory.RegisterAsync();

        var response = await client.GetAsync($"/api/v1/history?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task 撥到隔天_昨日出現在History且displayStreak正確()
    {
        var client = await _factory.RegisterAsync();
        var ids = await QuestIdsAsync(client);
        await CompleteSevenAsync(client, ids);

        _factory.Clock.Advance(TimeSpan.FromDays(1));

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        me.GetProperty("player").GetProperty("displayStreak").GetInt32().Should().Be(1);
        me.GetProperty("player").GetProperty("bestStreak").GetInt32().Should().Be(1);
        me.GetProperty("program").GetProperty("dayNumber").GetInt32().Should().Be(2);

        var history = await client.GetFromJsonAsync<JsonElement>("/api/v1/history?from=2026-09-28&to=2026-09-29");
        history.GetArrayLength().Should().Be(2);
        history[0].GetProperty("date").GetString().Should().Be("2026-09-28");
        history[0].GetProperty("isCleared").GetBoolean().Should().BeTrue();
        history[1].GetProperty("date").GetString().Should().Be("2026-09-29");
        history[1].GetProperty("completionRatio").GetDecimal().Should().Be(0);

        var today = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");
        today.GetProperty("date").GetString().Should().Be("2026-09-29");
        today.GetProperty("quests").EnumerateArray().Should().OnlyContain(q => !q.GetProperty("isDone").GetBoolean());
    }

    [Fact]
    public async Task 困難模式漏一天_出現Penalty且不降級()
    {
        var client = await _factory.RegisterAsync();
        await client.PatchAsJsonAsync("/api/v1/me", new { hardMode = true });

        _factory.Clock.Advance(TimeSpan.FromDays(1));

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        me.GetProperty("player").GetProperty("level").GetInt32().Should().Be(1);
        me.GetProperty("player").GetProperty("xp").GetInt32().Should().Be(0);
        var events = await client.GetFromJsonAsync<JsonElement>("/api/v1/xp-events");
        events.EnumerateArray().Should().ContainSingle(e => e.GetProperty("source").GetString() == "Penalty" && e.GetProperty("amount").GetInt32() == -15);
    }

    [Fact]
    public async Task ProgramRestart_開新週期_不重置等級()
    {
        var client = await _factory.RegisterAsync();
        var ids = await QuestIdsAsync(client);
        await SetProgressAsync(client, ids[0], 1);
        _factory.Clock.Advance(TimeSpan.FromDays(3));

        var response = await client.PostAsync("/api/v1/program/restart", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var program = await response.Content.ReadFromJsonAsync<JsonElement>();
        program.GetProperty("cycle").GetInt32().Should().Be(2);
        program.GetProperty("startDate").GetString().Should().Be("2026-10-01");
        program.GetProperty("dayNumber").GetInt32().Should().Be(1);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        me.GetProperty("player").GetProperty("xp").GetInt32().Should().Be(20);
        me.GetProperty("program").GetProperty("cycle").GetInt32().Should().Be(2);
    }
}
