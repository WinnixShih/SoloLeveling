using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using SoloLeveling.Api.Services;

namespace SoloLeveling.Api.Tests;

/// <summary>
/// 對著 Testcontainers 的 PostgreSQL 起整個 API；時間來源換成 <see cref="FakeTimeProvider"/> 讓測試可撥時間。
/// <paramref name="environment"/> 預設 Development，要驗證正式環境行為（例如 Swagger 關閉）時傳 "Production"。
/// </summary>
public sealed class ApiFactory(string connectionString, string environment = "Development") : WebApplicationFactory<Program>
{
    public const string JwtSecret = "test-secret-key-must-be-at-least-32-bytes-long!!";

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));

    /// <summary>抽卡亂數；預設永遠抽該等級的第一張卡，測試可改 <see cref="FixedRandom.Value"/>。</summary>
    public FixedRandom Rng { get; } = new();

    private readonly Dictionary<HttpClient, (string Email, DateTimeOffset IssuedAt)> _sessions = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("DatabaseConnectionString", connectionString);
        builder.UseSetting("JwtSecret", JwtSecret);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<Random>();
            services.AddSingleton<Random>(Rng);
            // 排程結算不在測試 host 內背景執行，避免跟測試本身的結算互相干擾；排程邏輯另有專屬測試
            var scheduler = services.Single(d => d.ImplementationType == typeof(SettlementScheduler));
            services.Remove(scheduler);
        });
    }

    /// <summary>
    /// 註冊一個隨機 Email 的新使用者並回傳帶 Bearer 的 client。
    /// 預設會透過 POST /goals 帶入全部 9 個基本任務，維持「註冊即有 9 個任務」的既有測試前提；要測引導流程時傳 <paramref name="seedBasicQuests"/> = false。
    /// </summary>
    public async Task<HttpClient> RegisterAsync(string timeZoneId = "UTC", bool seedBasicQuests = true)
    {
        var client = CreateClient();
        var email = $"{Guid.NewGuid():N}@test.local";
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "password123",
            displayName = "tester",
            timeZoneId,
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        _sessions[client] = (email, Clock.GetUtcNow());
        if (seedBasicQuests)
        {
            var seeded = await client.PostAsJsonAsync("/api/v1/goals", new { goals = Array.Empty<object>(), basicQuestIndexes = Enumerable.Range(0, 9).ToArray() });
            seeded.EnsureSuccessStatusCode();
        }

        return client;
    }

    /// <summary>
    /// 撥動假時鐘；權杖有效 7 天，撥超過 3 天後重新登入換發，讓長天數情境的 client 不會因權杖過期而 401。
    /// </summary>
    public async Task AdvanceAsync(HttpClient client, TimeSpan span)
    {
        Clock.Advance(span);
        var session = _sessions[client];
        if (Clock.GetUtcNow() - session.IssuedAt < TimeSpan.FromDays(3))
        {
            return;
        }

        var response = await CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = session.Email, password = "password123" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        _sessions[client] = (session.Email, Clock.GetUtcNow());
    }
}
