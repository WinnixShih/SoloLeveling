using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SoloLeveling.Api.Auth;

/// <summary>
/// 簽發 JWT 存取權杖（HS256，有效 7 天）。使用者 ID 放在 <c>sub</c> claim。
/// </summary>
/// <param name="options">應用程式設定（取 JwtSecret）。</param>
/// <param name="clock">時間來源。</param>
public class JwtTokenService(IOptions<AppOptions> options, TimeProvider clock)
{
    /// <summary>權杖有效期間。</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <summary>
    /// 為指定使用者簽發權杖。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <returns>JWT 字串。</returns>
    public string CreateToken(Guid userId)
    {
        var now = clock.GetUtcNow();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Value.JwtSecret));
        // 不放 nbf：IdentityModel 的有效期驗證用真實時鐘，測試撥假時間時會被判「尚未生效」
        var token = new JwtSecurityToken(
            claims: [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())],
            expires: now.Add(Lifetime).UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
