using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace SoloLeveling.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class QuestsApiTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture.ConnectionString);

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task Post_Count類型缺targetValue_回400()
    {
        var client = await _factory.RegisterAsync();

        var response = await client.PostAsJsonAsync("/api/v1/quests", new { name = "伏地挺身", statType = "STR", difficulty = "Hard", questType = "Count" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Post_新增Check任務_回201並排在最後()
    {
        var client = await _factory.RegisterAsync();

        var response = await client.PostAsJsonAsync("/api/v1/quests", new { name = "冥想", statType = "SPI", difficulty = "Easy", questType = "Check" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        created.GetProperty("id").GetGuid().Should().NotBeEmpty();
        created.GetProperty("sortOrder").GetInt32().Should().Be(9);
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/quests");
        list.GetArrayLength().Should().Be(10);
        list[9].GetProperty("name").GetString().Should().Be("冥想");
    }

    [Fact]
    public async Task Put_修改任務名稱與難度_回200()
    {
        var client = await _factory.RegisterAsync();
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/quests");
        var id = list[0].GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync($"/api/v1/quests/{id}", new { name = "23:00 前上床", statType = "VIT", difficulty = "Hard", questType = "Check" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("name").GetString().Should().Be("23:00 前上床");
        updated.GetProperty("difficulty").GetString().Should().Be("Hard");
    }

    [Fact]
    public async Task Delete_封存後不再出現在清單()
    {
        var client = await _factory.RegisterAsync();
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/quests");
        var id = list[0].GetProperty("id").GetGuid();

        var response = await client.DeleteAsync($"/api/v1/quests/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/quests");
        after.GetArrayLength().Should().Be(8);
        after.EnumerateArray().Should().NotContain(q => q.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Reorder_依questIds重排()
    {
        var client = await _factory.RegisterAsync();
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/quests");
        var ids = list.EnumerateArray().Select(q => q.GetProperty("id").GetGuid()).ToList();
        ids.Reverse();

        var response = await client.PutAsJsonAsync("/api/v1/quests/reorder", new { questIds = ids });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/quests");
        after.EnumerateArray().Select(q => q.GetProperty("id").GetGuid()).Should().Equal(ids);
    }

    [Fact]
    public async Task 操作別人的任務_回404()
    {
        var owner = await _factory.RegisterAsync();
        var other = await _factory.RegisterAsync();
        var list = await owner.GetFromJsonAsync<JsonElement>("/api/v1/quests");
        var id = list[0].GetProperty("id").GetGuid();

        var response = await other.DeleteAsync($"/api/v1/quests/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
