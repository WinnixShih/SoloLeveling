using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SoloLeveling.Api;
using SoloLeveling.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<AppOptions>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var appOptions = sp.GetRequiredService<IOptions<AppOptions>>().Value;
    options.UseNpgsql(appOptions.DatabaseConnectionString);
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 啟動時自動套用 migration，讓 docker compose up 後即可使用
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

app.Run();

/// <summary>
/// 讓整合測試的 <c>WebApplicationFactory</c> 可以參照到進入點。
/// </summary>
public partial class Program
{
}
