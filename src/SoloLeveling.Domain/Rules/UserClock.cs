namespace SoloLeveling.Domain.Rules;

/// <summary>
/// 把 UTC 時間換算成使用者時區的「日期」；這是全專案唯一決定「今日」的地方。
/// </summary>
public static class UserClock
{
    /// <summary>
    /// 取得指定時間在使用者時區下的日期。
    /// </summary>
    /// <param name="utc">UTC 時間。</param>
    /// <param name="timeZoneId">IANA 時區 ID（例：<c>Asia/Taipei</c>）。</param>
    /// <returns>使用者時區下的日期。</returns>
    /// <exception cref="TimeZoneNotFoundException">時區 ID 不存在。</exception>
    public static DateOnly DateOf(DateTimeOffset utc, string timeZoneId)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(utc, tz);
        return DateOnly.FromDateTime(local.DateTime);
    }
}
