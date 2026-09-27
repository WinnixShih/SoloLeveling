using System.Security.Cryptography;

namespace SoloLeveling.Infrastructure;

/// <summary>
/// PBKDF2-SHA256 密碼雜湊。儲存格式 <c>pbkdf2-sha256$迭代次數$salt(Base64)$hash(Base64)</c>，參數隨雜湊一起存，日後調高迭代次數不影響舊資料驗證。
/// </summary>
public static class PasswordHasher
{
    private const string Algorithm = "pbkdf2-sha256";
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    /// <summary>
    /// 產生密碼雜湊。
    /// </summary>
    /// <param name="password">明文密碼。</param>
    /// <returns>自描述的雜湊字串。</returns>
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Algorithm}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// 驗證密碼是否符合雜湊。
    /// </summary>
    /// <param name="password">明文密碼。</param>
    /// <param name="stored">先前由 <see cref="Hash"/> 產生的字串。</param>
    /// <returns>是否相符；格式不正確一律回 false。</returns>
    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Algorithm || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
