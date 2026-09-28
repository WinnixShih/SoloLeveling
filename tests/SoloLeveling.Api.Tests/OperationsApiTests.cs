using System.Net;
using FluentAssertions;

namespace SoloLeveling.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class OperationsApiTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Health_未登入_回200()
    {
        using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task Swagger_Development環境_回200()
    {
        using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Swagger_Production環境_回404()
    {
        using var factory = new ApiFactory(fixture.ConnectionString, "Production");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/app.js")]
    public async Task StaticFiles_根路徑與appjs_都帶no_cache(string path)
    {
        using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoCache.Should().BeTrue();
    }
}
