using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;

namespace SoloLeveling.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class AccountApiTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture.ConnectionString);

    public void Dispose()
    {
        _factory.Dispose();
    }

    private static object RegisterBody(string email, string password = "password123")
    {
        return new { email, password, displayName = "小明", timeZoneId = "Asia/Taipei" };
    }

    [Fact]
    public async Task Register_成功_回201與token與user()
    {
        var client = _factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@test.local";

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", RegisterBody(email));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("token").GetString().Should().NotBeNullOrEmpty();
        var user = body.GetProperty("user");
        user.GetProperty("email").GetString().Should().Be(email);
        user.GetProperty("displayName").GetString().Should().Be("小明");
        user.GetProperty("timeZoneId").GetString().Should().Be("Asia/Taipei");
    }

    [Fact]
    public async Task Register_重複Email_回409()
    {
        var client = _factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@test.local";
        await client.PostAsJsonAsync("/api/v1/auth/register", RegisterBody(email));

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", RegisterBody(email.ToUpperInvariant()));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("EmailTaken");
    }

    [Fact]
    public async Task Register_密碼少於8碼_回400()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", RegisterBody($"{Guid.NewGuid():N}@test.local", "short"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_密碼錯誤_回401()
    {
        var client = _factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@test.local";
        await client.PostAsJsonAsync("/api/v1/auth/register", RegisterBody(email));

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "wrong-password" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("InvalidCredentials");
    }

    [Fact]
    public async Task Login_成功_token可用於GetMe()
    {
        var client = _factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@test.local";
        await client.PostAsJsonAsync("/api/v1/auth/register", RegisterBody(email));

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "password123" });

        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var me = await client.GetAsync("/api/v1/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Token有效期以注入時鐘判定_未過期可用_過期後回401()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);

        _factory.Clock.Advance(Auth.JwtTokenService.Lifetime - TimeSpan.FromHours(1));
        var beforeExpiry = await client.GetAsync("/api/v1/me");
        _factory.Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(2));
        var afterExpiry = await client.GetAsync("/api/v1/me");

        beforeExpiry.StatusCode.Should().Be(HttpStatusCode.OK);
        afterExpiry.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token沒有exp_回401()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.JwtSecret));
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_未帶token_回401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_新玩家_初始狀態正確()
    {
        var client = await _factory.RegisterAsync("Asia/Taipei");

        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");

        var player = body.GetProperty("player");
        player.GetProperty("level").GetInt32().Should().Be(1);
        player.GetProperty("xp").GetInt32().Should().Be(0);
        player.GetProperty("xpNeeded").GetInt32().Should().Be(100);
        player.GetProperty("rank").GetString().Should().Be("E");
        player.GetProperty("title").GetString().Should().Be("新手");
        var stats = player.GetProperty("stats");
        foreach (var name in new[] { "str", "vit", "int", "wil", "spi" })
        {
            stats.GetProperty(name).GetInt32().Should().Be(10);
        }

        player.GetProperty("hardMode").GetBoolean().Should().BeFalse();
        player.GetProperty("displayStreak").GetInt32().Should().Be(0);
        player.GetProperty("bestStreak").GetInt32().Should().Be(0);
        player.GetProperty("totalCompleted").GetInt32().Should().Be(0);

        var program = body.GetProperty("program");
        program.GetProperty("cycle").GetInt32().Should().Be(1);
        program.GetProperty("dayNumber").GetInt32().Should().Be(1);
        program.GetProperty("lengthDays").GetInt32().Should().Be(66);
        program.GetProperty("isCompleted").GetBoolean().Should().BeFalse();
        // 2026-09-28 10:00 UTC → 台北 18:00，同日
        program.GetProperty("startDate").GetString().Should().Be("2026-09-28");
    }

    [Fact]
    public async Task Register_經ApiFactory帶入基本任務_有9個任務()
    {
        var client = await _factory.RegisterAsync();

        var quests = await client.GetFromJsonAsync<JsonElement>("/api/v1/quests");

        quests.GetArrayLength().Should().Be(9);
        var first = quests[0];
        first.GetProperty("name").GetString().Should().Be("23:30 前上床睡覺");
        first.GetProperty("statType").GetString().Should().Be("VIT");
        first.GetProperty("difficulty").GetString().Should().Be("Normal");
        first.GetProperty("questType").GetString().Should().Be("Check");
        var water = quests[2];
        water.GetProperty("name").GetString().Should().Be("喝水");
        water.GetProperty("questType").GetString().Should().Be("Count");
        water.GetProperty("targetValue").GetDecimal().Should().Be(8);
        water.GetProperty("step").GetDecimal().Should().Be(1);
        water.GetProperty("unit").GetString().Should().Be("杯");
    }

    [Fact]
    public async Task PatchMe_更新顯示名稱與時區()
    {
        var client = await _factory.RegisterAsync();

        var response = await client.PatchAsJsonAsync("/api/v1/me", new { displayName = "新名字", timeZoneId = "America/New_York" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("user");
        user.GetProperty("displayName").GetString().Should().Be("新名字");
        user.GetProperty("timeZoneId").GetString().Should().Be("America/New_York");
    }

    [Fact]
    public async Task PatchMe_不合法時區_回400()
    {
        var client = await _factory.RegisterAsync();

        var response = await client.PatchAsJsonAsync("/api/v1/me", new { timeZoneId = "Mars/Olympus" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PatchMe_切換困難模式_回傳hardMode為true()
    {
        var client = await _factory.RegisterAsync();

        var response = await client.PatchAsJsonAsync("/api/v1/me", new { hardMode = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var player = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("player");
        player.GetProperty("hardMode").GetBoolean().Should().BeTrue();
    }
}
