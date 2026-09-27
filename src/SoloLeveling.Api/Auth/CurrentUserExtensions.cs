using System.Security.Claims;

namespace SoloLeveling.Api.Auth;

/// <summary>
/// 從已驗證的 <see cref="ClaimsPrincipal"/> 取出使用者 ID。
/// </summary>
public static class CurrentUserExtensions
{
    /// <summary>
    /// 取得 <c>sub</c> claim 的使用者 ID。
    /// </summary>
    /// <param name="user">目前請求的使用者。</param>
    /// <returns>使用者 ID。</returns>
    /// <exception cref="InvalidOperationException">未驗證或 claim 格式不正確；在 <c>[Authorize]</c> 之後不應發生。</exception>
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var id))
        {
            throw new InvalidOperationException("缺少有效的 sub claim");
        }

        return id;
    }
}
