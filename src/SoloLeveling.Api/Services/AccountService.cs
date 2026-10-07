using Microsoft.EntityFrameworkCore;
using Npgsql;
using SoloLeveling.Api.Auth;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Errors;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;
using SoloLeveling.Infrastructure;
using ProgramEntity = SoloLeveling.Domain.Entities.Program;

namespace SoloLeveling.Api.Services;

/// <summary>
/// 註冊與登入。
/// </summary>
/// <param name="db">DbContext。</param>
/// <param name="clock">時間來源。</param>
/// <param name="tokens">JWT 簽發。</param>
public class AccountService(AppDbContext db, TimeProvider clock, JwtTokenService tokens)
{
    /// <summary>密碼最少字數。</summary>
    public const int MinPasswordLength = 8;

    /// <summary>
    /// 註冊：建立 User、Player 與第 1 個 66 天週期；任務由引導流程（POST /goals）建立。
    /// </summary>
    /// <param name="request">註冊請求。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>權杖與使用者。</returns>
    /// <exception cref="ApiErrorException">欄位不合法（400）或 Email 已存在（409）。</exception>
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim();
        if (!email.Contains('@'))
        {
            throw ApiErrorException.BadRequest("InvalidEmail", "Email 格式不正確");
        }

        if (request.Password.Length < MinPasswordLength)
        {
            throw ApiErrorException.BadRequest("WeakPassword", $"密碼至少 {MinPasswordLength} 碼");
        }

        var displayName = request.DisplayName.Trim();
        if (displayName.Length is 0 or > 40)
        {
            throw ApiErrorException.BadRequest("InvalidDisplayName", "顯示名稱需為 1–40 字");
        }

        var timeZoneId = ValidateTimeZone(request.TimeZoneId ?? "Asia/Taipei");

        if (await db.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower(), ct))
        {
            throw ApiErrorException.Conflict("EmailTaken", "此 Email 已被註冊");
        }

        var now = clock.GetUtcNow();
        var today = UserClock.DateOf(now, timeZoneId);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = PasswordHasher.Hash(request.Password),
            DisplayName = displayName,
            TimeZoneId = timeZoneId,
            CreatedAt = now,
        };
        db.Users.Add(user);
        db.Players.Add(new Player { UserId = user.Id, CreatedAt = now });
        db.Programs.Add(new ProgramEntity { Id = Guid.NewGuid(), UserId = user.Id, StartDate = today, Cycle = 1, IsActive = true, CreatedAt = now });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Users_Email" })
        {
            // 預查與寫入之間被同 Email 的併發註冊搶先，唯一索引擋下
            throw ApiErrorException.Conflict("EmailTaken", "此 Email 已被註冊");
        }

        return new AuthResponse(tokens.CreateToken(user.Id), user.ToDto());
    }

    /// <summary>
    /// 登入。
    /// </summary>
    /// <param name="request">登入請求。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>權杖與使用者。</returns>
    /// <exception cref="ApiErrorException">帳密錯誤（401）。</exception>
    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), ct);
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw ApiErrorException.Unauthorized("InvalidCredentials", "Email 或密碼錯誤");
        }

        return new AuthResponse(tokens.CreateToken(user.Id), user.ToDto());
    }

    /// <summary>
    /// 確認 IANA 時區 ID 存在。
    /// </summary>
    /// <param name="timeZoneId">時區 ID。</param>
    /// <returns>原字串。</returns>
    /// <exception cref="ApiErrorException">時區不存在（400）。</exception>
    public static string ValidateTimeZone(string timeZoneId)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _))
        {
            throw ApiErrorException.BadRequest("InvalidTimeZone", $"未知的時區：{timeZoneId}");
        }

        return timeZoneId;
    }
}
