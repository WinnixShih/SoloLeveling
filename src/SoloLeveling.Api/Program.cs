using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SoloLeveling.Api;
using SoloLeveling.Api.Auth;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Errors;
using SoloLeveling.Api.Services;
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
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<SettlementService>();
builder.Services.AddScoped<TodayContextLoader>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<QuestService>();
builder.Services.AddScoped<TodayService>();
builder.Services.AddScoped<HistoryService>();
builder.Services.AddScoped<ProgramService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var secret = builder.Configuration["JwtSecret"] ?? string.Empty;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
        // 未驗證時也回統一的錯誤格式
        options.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return context.Response.WriteAsJsonAsync(new ErrorResponse(new ErrorBody("Unauthorized", "需要有效的 Bearer token")));
            },
        };
    });
builder.Services.AddAuthorization();

builder.Services
    .AddControllers(options => options.SuppressAsyncSuffixInActionNames = false)
    .AddJsonOptions(options =>
    {
        // StatType 用代碼（VIT 等），要排在通用的字串 enum converter 前面才會被選到
        options.JsonSerializerOptions.Converters.Add(new StatTypeJsonConverter());
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        // 模型繫結失敗也回統一的錯誤格式
        options.InvalidModelStateResponseFactory = context =>
        {
            var message = string.Join("；", context.ModelState
                .SelectMany(kv => kv.Value!.Errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? kv.Key : e.ErrorMessage)));
            return new BadRequestObjectResult(new ErrorResponse(new ErrorBody("ValidationFailed", message)));
        };
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 啟動時自動套用 migration，讓 docker compose up 後即可使用
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseSwagger();
app.UseSwaggerUI();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

/// <summary>
/// 讓整合測試的 <c>WebApplicationFactory</c> 可以參照到進入點。
/// </summary>
public partial class Program
{
}
