using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace SoloLeveling.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ProgressionApiTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture.ConnectionString);

    public void Dispose()
    {
        _factory.Dispose();
    }

    private async Task<(HttpClient Client, Guid BedId, Guid ReadingId)> SetupAsync()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);
        var response = await client.PostAsJsonAsync("/api/v1/goals", new
        {
            goals = new object[]
            {
                new { category = "Routine", answers = new { currentBedtime = "01:00", targetBedtime = "00:00", currentWakeTime = "08:00", targetWakeTime = "08:00", lengthDays = 30 } },
                new { category = "Reading", answers = new { currentMinutes = 10, targetMinutes = 30, lengthDays = 30 } },
            },
            basicQuestIndexes = Array.Empty<int>(),
        });
        response.EnsureSuccessStatusCode();
        var today = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");
        var quests = today.GetProperty("quests").EnumerateArray().ToList();
        return (client, quests[0].GetProperty("id").GetGuid(), quests[2].GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> QuestOfAsync(HttpClient client, Guid id)
    {
        var today = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");
        return today.GetProperty("quests").EnumerateArray().Single(q => q.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task 連續達標3天後_第4天升到第2階_中間漏一天則不升()
    {
        var (client, bedId, _) = await SetupAsync();

        // 第 1、2、3 天各勾一次
        for (var day = 0; day < 3; day++)
        {
            (await QuestOfAsync(client, bedId)).GetProperty("name").GetString().Should().Be("00:55 前上床睡覺");
            await client.PutAsJsonAsync($"/api/v1/today/quests/{bedId}/progress", new { value = 1 });
            await _factory.AdvanceAsync(client, TimeSpan.FromDays(1));
        }

        // 第 4 天：第 2 階
        var day4 = await QuestOfAsync(client, bedId);
        day4.GetProperty("name").GetString().Should().Be("00:50 前上床睡覺");
        day4.GetProperty("progression").GetProperty("stage").GetInt32().Should().Be(2);

        // 第 4、5 天沒勾，第 6 天勾 → 達標 4 天，仍第 2 階
        await _factory.AdvanceAsync(client, TimeSpan.FromDays(2));
        await client.PutAsJsonAsync($"/api/v1/today/quests/{bedId}/progress", new { value = 1 });
        await _factory.AdvanceAsync(client, TimeSpan.FromDays(1));
        var day7 = await QuestOfAsync(client, bedId);
        day7.GetProperty("progression").GetProperty("stage").GetInt32().Should().Be(2);
        day7.GetProperty("name").GetString().Should().Be("00:50 前上床睡覺");

        // 再達標 2 天 → 6 天 → 第 3 階 00:45
        for (var i = 0; i < 2; i++)
        {
            await client.PutAsJsonAsync($"/api/v1/today/quests/{bedId}/progress", new { value = 1 });
            await _factory.AdvanceAsync(client, TimeSpan.FromDays(1));
        }

        (await QuestOfAsync(client, bedId)).GetProperty("name").GetString().Should().Be("00:45 前上床睡覺");
    }

    [Fact]
    public async Task 今天自己的完成_不影響今天的目標()
    {
        var (client, bedId, _) = await SetupAsync();
        await client.PutAsJsonAsync($"/api/v1/today/quests/{bedId}/progress", new { value = 1 });
        await client.PutAsJsonAsync($"/api/v1/today/quests/{bedId}/progress", new { value = 0 });
        await client.PutAsJsonAsync($"/api/v1/today/quests/{bedId}/progress", new { value = 1 });

        (await QuestOfAsync(client, bedId)).GetProperty("progression").GetProperty("stage").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task SetProgress_Count漸進任務_以當階目標判定並寫入快照()
    {
        var (client, _, readingId) = await SetupAsync();

        var r11 = await (await client.PutAsJsonAsync($"/api/v1/today/quests/{readingId}/progress", new { value = 11 })).Content.ReadFromJsonAsync<JsonElement>();
        r11.GetProperty("quests").EnumerateArray().Single(q => q.GetProperty("id").GetGuid() == readingId).GetProperty("isDone").GetBoolean().Should().BeFalse();

        var r12 = await (await client.PutAsJsonAsync($"/api/v1/today/quests/{readingId}/progress", new { value = 12 })).Content.ReadFromJsonAsync<JsonElement>();
        r12.GetProperty("quests").EnumerateArray().Single(q => q.GetProperty("id").GetGuid() == readingId).GetProperty("isDone").GetBoolean().Should().BeTrue();

        await using var db = fixture.CreateDbContext();
        var progress = db.QuestProgresses.Single(p => p.QuestId == readingId);
        progress.TargetSnapshot.Should().Be(12);
    }

    [Fact]
    public async Task 到最後一階後_目標固定在終點()
    {
        var (client, bedId, _) = await SetupAsync();
        for (var day = 0; day < 40; day++)
        {
            await client.PutAsJsonAsync($"/api/v1/today/quests/{bedId}/progress", new { value = 1 });
            await _factory.AdvanceAsync(client, TimeSpan.FromDays(1));
        }

        var quest = await QuestOfAsync(client, bedId);
        quest.GetProperty("name").GetString().Should().Be("00:00 前上床睡覺");
        quest.GetProperty("progression").GetProperty("stage").GetInt32().Should().Be(10);
        quest.GetProperty("progression").GetProperty("stageCount").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task PutQuest_漸進任務改目標欄位_回400_改難度成功()
    {
        var (client, _, readingId) = await SetupAsync();
        var quests = await client.GetFromJsonAsync<JsonElement>("/api/v1/quests");
        var reading = quests.EnumerateArray().Single(q => q.GetProperty("id").GetGuid() == readingId);
        reading.GetProperty("goalId").ValueKind.Should().NotBe(JsonValueKind.Null);

        var locked = await client.PutAsJsonAsync($"/api/v1/quests/{readingId}", new { name = "閱讀", statType = "INT", difficulty = "Normal", questType = "Count", targetValue = 99, step = 5, unit = "分鐘" });
        locked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await locked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString().Should().Be("ProgressionQuestLocked");

        var ok = await client.PutAsJsonAsync($"/api/v1/quests/{readingId}", new { name = "閱讀", statType = "INT", difficulty = "Hard", questType = "Count", targetValue = (decimal?)null, step = 5, unit = "分鐘" });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("difficulty").GetString().Should().Be("Hard");
    }

    [Fact]
    public async Task DeleteQuest_單獨封存漸進任務_目標仍在()
    {
        var (client, bedId, _) = await SetupAsync();

        (await client.DeleteAsync($"/api/v1/quests/{bedId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var goals = await client.GetFromJsonAsync<JsonElement>("/api/v1/goals");
        var routine = goals.GetProperty("goals").EnumerateArray().Single(g => g.GetProperty("category").GetString() == "Routine");
        routine.GetProperty("quests").EnumerateArray().Single(q => q.GetProperty("id").GetGuid() == bedId).GetProperty("isArchived").GetBoolean().Should().BeTrue();
    }
}
