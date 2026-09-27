using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace SoloLeveling.Api.Tests;

/// <summary>
/// 對著 Testcontainers 的 PostgreSQL 起整個 API；時間來源換成 <see cref="FakeTimeProvider"/> 讓測試可撥時間。
/// </summary>
public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    public const string JwtSecret = "test-secret-key-must-be-at-least-32-bytes-long!!";

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DatabaseConnectionString", connectionString);
        builder.UseSetting("JwtSecret", JwtSecret);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    /// <summary>
    /// 註冊一個隨機 Email 的新使用者並回傳帶 Bearer 的 client。
    /// </summary>
    public async Task<HttpClient> RegisterAsync(string timeZoneId = "UTC")
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = $"{Guid.NewGuid():N}@test.local",
            password = "password123",
            displayName = "tester",
            timeZoneId,
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        return client;
    }
}
