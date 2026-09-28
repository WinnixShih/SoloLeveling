using Microsoft.EntityFrameworkCore;
using SoloLeveling.Infrastructure;
using Testcontainers.PostgreSql;

namespace SoloLeveling.Api.Tests;

/// <summary>
/// 以 Testcontainers 起一個 PostgreSQL 16，透過 <see cref="PostgresCollection"/> 讓所有測試類別共用同一個容器；每個測試自行建立獨立的 DbContext。
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
