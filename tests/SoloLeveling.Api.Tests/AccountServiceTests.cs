using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SoloLeveling.Api.Auth;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Errors;
using SoloLeveling.Api.Services;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Tests;

[Collection(PostgresCollection.Name)]
public class AccountServiceTests(PostgresFixture fixture)
{
    /// <summary>
    /// 在第一次存檔前，由另一條連線搶先寫入同 Email 的使用者，確定地重現「通過預查後被搶先註冊」的競態。
    /// </summary>
    private sealed class RivalInsertInterceptor(PostgresFixture fixture, string email) : SaveChangesInterceptor
    {
        private bool _fired;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!_fired)
            {
                _fired = true;
                await using var rival = fixture.CreateDbContext();
                rival.Users.Add(new User { Id = Guid.NewGuid(), Email = email, PasswordHash = "x", DisplayName = "rival", TimeZoneId = "UTC", CreatedAt = DateTimeOffset.UnixEpoch });
                await rival.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }

    [Fact]
    public async Task Register_預查通過後同Email被搶先寫入_丟409EmailTaken()
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .AddInterceptors(new RivalInsertInterceptor(fixture, email))
            .Options;
        await using var db = new AppDbContext(options);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));
        var jwt = new JwtTokenService(Options.Create(new AppOptions { JwtSecret = ApiFactory.JwtSecret }), clock);
        var service = new AccountService(db, clock, jwt);

        var act = () => service.RegisterAsync(new RegisterRequest(email, "password123", "小明", "UTC"), CancellationToken.None);

        var error = (await act.Should().ThrowAsync<ApiErrorException>()).Which;
        error.Code.Should().Be("EmailTaken");
        error.StatusCode.Should().Be(409);
    }
}
