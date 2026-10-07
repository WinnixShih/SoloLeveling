using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace SoloLeveling.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class GoalsApiTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture.ConnectionString);

    public void Dispose()
    {
        _factory.Dispose();
    }

    private static object RoutineGoal(string bed = "01:00", string targetBed = "00:00", string wake = "08:00", string targetWake = "07:00", int days = 30)
    {
        return new
        {
            category = "Routine",
            answers = new { currentBedtime = bed, targetBedtime = targetBed, currentWakeTime = wake, targetWakeTime = targetWake, lengthDays = days },
        };
    }

    private static object ReadingGoal(int current = 10, int target = 30, int days = 30)
    {
        return new { category = "Reading", answers = new { currentMinutes = current, targetMinutes = target, lengthDays = days } };
    }

    [Fact]
    public async Task Register_不帶基本任務_needsOnboarding為true且今日無任務()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        var today = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");

        me.GetProperty("needsOnboarding").GetBoolean().Should().BeTrue();
        today.GetProperty("quests").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Register_預設帶基本任務_needsOnboarding為false且有9個任務()
    {
        var client = await _factory.RegisterAsync();

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        var today = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");

        me.GetProperty("needsOnboarding").GetBoolean().Should().BeFalse();
        today.GetProperty("quests").GetArrayLength().Should().Be(9);
        today.GetProperty("quests")[0].GetProperty("name").GetString().Should().Be("23:30 前上床睡覺");
    }

    [Fact]
    public async Task GetCategories_回四個類別與九個基本任務()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);

        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/goals/categories");

        var categories = body.GetProperty("categories");
        categories.GetArrayLength().Should().Be(4);
        categories[0].GetProperty("category").GetString().Should().Be("Routine");
        categories[0].GetProperty("title").GetString().Should().Be("作息");
        categories[0].GetProperty("questions").EnumerateArray().Last().GetProperty("key").GetString().Should().Be("lengthDays");
        categories[0].GetProperty("replacesBasicQuestIndexes").EnumerateArray().Select(e => e.GetInt32()).Should().Equal(0, 1);
        body.GetProperty("basicQuests").GetArrayLength().Should().Be(9);
        body.GetProperty("basicQuests")[2].GetProperty("name").GetString().Should().Be("喝水");
    }

    [Fact]
    public async Task PostPreview_回階段摘要且不寫入()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);

        var response = await client.PostAsJsonAsync("/api/v1/goals/preview", new { goals = new[] { RoutineGoal() }, basicQuestIndexes = Array.Empty<int>() });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var quest = body.GetProperty("goals")[0].GetProperty("quests")[0];
        quest.GetProperty("name").GetString().Should().Be("00:55 前上床睡覺");
        quest.GetProperty("startLabel").GetString().Should().Be("01:00");
        quest.GetProperty("endLabel").GetString().Should().Be("00:00");
        quest.GetProperty("stageCount").GetInt32().Should().Be(10);
        quest.GetProperty("daysPerStep").GetInt32().Should().Be(3);
        quest.GetProperty("stepLabel").GetString().Should().Be("提早 5 分鐘");

        // 起床以 18:00 為基準編碼（ValueKind = TimeOfDayEvening），stepLabel 仍是「提早 N 分鐘」而非把分鐘數誤當成絕對時刻
        var wake = body.GetProperty("goals")[0].GetProperty("quests")[1];
        wake.GetProperty("name").GetString().Should().Be("07:55 前起床");
        wake.GetProperty("startLabel").GetString().Should().Be("08:00");
        wake.GetProperty("endLabel").GetString().Should().Be("07:00");
        wake.GetProperty("stepLabel").GetString().Should().Be("提早 5 分鐘");

        var today = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");
        today.GetProperty("quests").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task PostGoals_建立作息與閱讀_今日出現任務且needsOnboarding變false()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);

        var response = await client.PostAsJsonAsync("/api/v1/goals", new { goals = new[] { RoutineGoal(), ReadingGoal() }, basicQuestIndexes = new[] { 2, 8 } });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var goals = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("goals");
        goals.GetArrayLength().Should().Be(2);
        // 兩個目標在同一次請求內建立、CreatedAt 相同；GoalService.BuildListAsync 以任務的最小 SortOrder 當次要排序鍵還原建立順序
        goals[0].GetProperty("category").GetString().Should().Be("Routine");
        goals[0].GetProperty("quests")[0].GetProperty("stage").GetInt32().Should().Be(1);
        goals[0].GetProperty("quests")[0].GetProperty("stageCount").GetInt32().Should().Be(10);
        goals[1].GetProperty("category").GetString().Should().Be("Reading");

        var today = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");
        var names = today.GetProperty("quests").EnumerateArray().Select(q => q.GetProperty("name").GetString()).ToList();
        names.Should().Equal("00:55 前上床睡覺", "07:55 前起床", "閱讀", "喝水", "寫下今天的三件好事");
        var reading = today.GetProperty("quests")[2];
        reading.GetProperty("targetValue").GetDecimal().Should().Be(12);
        reading.GetProperty("progression").GetProperty("targetLabel").GetString().Should().Be("12 分鐘");
        today.GetProperty("quests")[3].GetProperty("progression").ValueKind.Should().Be(JsonValueKind.Null);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        me.GetProperty("needsOnboarding").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetGoals_同一請求建立多個目標_依建立順序排序()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);
        await client.PostAsJsonAsync("/api/v1/goals", new { goals = new[] { RoutineGoal(), ReadingGoal() }, basicQuestIndexes = Array.Empty<int>() });

        var goals = (await client.GetFromJsonAsync<JsonElement>("/api/v1/goals")).GetProperty("goals");

        goals.GetArrayLength().Should().Be(2);
        goals[0].GetProperty("category").GetString().Should().Be("Routine");
        goals[1].GetProperty("category").GetString().Should().Be("Reading");
    }

    [Fact]
    public async Task PostGoals_基本任務含被取代的索引_回400()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);

        var response = await client.PostAsJsonAsync("/api/v1/goals", new { goals = new[] { RoutineGoal() }, basicQuestIndexes = new[] { 0 } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString().Should().Be("BasicQuestReplaced");
    }

    [Fact]
    public async Task PostGoals_同類別已有進行中_回409()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);
        await client.PostAsJsonAsync("/api/v1/goals", new { goals = new[] { ReadingGoal() }, basicQuestIndexes = Array.Empty<int>() });

        var response = await client.PostAsJsonAsync("/api/v1/goals", new { goals = new[] { ReadingGoal() }, basicQuestIndexes = Array.Empty<int>() });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString().Should().Be("GoalAlreadyActive");
    }

    [Fact]
    public async Task PostGoals_回答格式錯_回400()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);

        var response = await client.PostAsJsonAsync("/api/v1/goals", new
        {
            goals = new[] { new { category = "Routine", answers = new { currentBedtime = "1:00", targetBedtime = "00:00", currentWakeTime = "08:00", targetWakeTime = "07:00", lengthDays = "abc" } } },
            basicQuestIndexes = Array.Empty<int>(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var code = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString();
        code.Should().BeOneOf("InvalidLengthDays", "InvalidTime");
    }

    [Fact]
    public async Task PostGoals_帶未知的回答鍵_回400且不建立目標()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);

        var response = await client.PostAsJsonAsync("/api/v1/goals", new
        {
            goals = new[] { new { category = "Reading", answers = new { currentMinutes = 10, targetMinutes = 30, lengthDays = 30, bogus = "x" } } },
            basicQuestIndexes = Array.Empty<int>(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString().Should().Be("UnknownAnswer");
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/goals")).GetProperty("goals").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task PostGoals_未知類別_回400()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);

        var response = await client.PostAsJsonAsync("/api/v1/goals", new { goals = new[] { new { category = "Cooking", answers = new { lengthDays = 30 } } }, basicQuestIndexes = Array.Empty<int>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteGoal_封存後任務消失且同類別可重建()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);
        var created = await (await client.PostAsJsonAsync("/api/v1/goals", new { goals = new[] { ReadingGoal() }, basicQuestIndexes = new[] { 2 } })).Content.ReadFromJsonAsync<JsonElement>();
        var goalId = created.GetProperty("goals")[0].GetProperty("id").GetGuid();
        var waterId = (await client.GetFromJsonAsync<JsonElement>("/api/v1/today")).GetProperty("quests").EnumerateArray()
            .Single(q => q.GetProperty("name").GetString() == "喝水").GetProperty("id").GetGuid();
        var done = await client.PutAsJsonAsync($"/api/v1/today/quests/{waterId}/progress", new { value = 8 });
        (await done.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("completionRatio").GetDecimal().Should().BeLessThan(1m);

        var del = await client.DeleteAsync($"/api/v1/goals/{goalId}");

        del.StatusCode.Should().Be(HttpStatusCode.OK);
        var today = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");
        today.GetProperty("quests").EnumerateArray().Select(q => q.GetProperty("name").GetString()).Should().Equal("喝水");
        // 分母只剩已完成的喝水
        today.GetProperty("completionRatio").GetDecimal().Should().Be(1m);
        var goals = await client.GetFromJsonAsync<JsonElement>("/api/v1/goals");
        goals.GetProperty("goals").GetArrayLength().Should().Be(0);

        var again = await client.PostAsJsonAsync("/api/v1/goals", new { goals = new[] { ReadingGoal() }, basicQuestIndexes = Array.Empty<int>() });
        again.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task DeleteGoal_不存在_回404()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);

        var del = await client.DeleteAsync($"/api/v1/goals/{Guid.NewGuid()}");

        del.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
