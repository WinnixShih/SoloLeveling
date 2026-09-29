using System.Globalization;

namespace SoloLeveling.Domain.Rules;

/// <summary>
/// 時間點的編碼：存成「距基準時刻的分鐘數」（0–1439）。就寢時間以中午 12:00 為基準，起床時間以 18:00 為基準（見
/// <see cref="SoloLeveling.Domain.ProgressionValueKind.TimeOfDayEvening"/>），兩者皆跨午夜後仍單調，「提早」就是數值變小。
/// </summary>
public static class TimeOfDay
{
    /// <summary>一天的分鐘數。</summary>
    public const int MinutesPerDay = 1440;

    /// <summary>
    /// 把 <c>HH:MM</c> 轉成距基準時刻的分鐘數。
    /// </summary>
    /// <param name="text">兩位小時、冒號、兩位分鐘，例如 <c>01:00</c>。</param>
    /// <param name="baseHour">基準時刻的小時（0–23），預設中午 12 點。</param>
    /// <returns>0–1439。</returns>
    /// <exception cref="DomainValidationException">格式不是 HH:MM 或超出範圍。</exception>
    public static int Parse(string text, int baseHour = 12)
    {
        if (text.Length != 5 || text[2] != ':'
            || !int.TryParse(text.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hour)
            || !int.TryParse(text.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minute)
            || hour > 23 || minute > 59)
        {
            throw new DomainValidationException("InvalidTime", $"時間格式須為 HH:MM，收到「{text}」");
        }

        return ((hour - baseHour + 24) % 24) * 60 + minute;
    }

    /// <summary>
    /// 把距基準時刻的分鐘數轉回 <c>HH:MM</c>。
    /// </summary>
    /// <param name="minutesFromBase">0–1439；有小數會四捨五入。</param>
    /// <param name="baseHour">基準時刻的小時（0–23），預設中午 12 點。</param>
    /// <returns><c>HH:MM</c>。</returns>
    public static string Format(decimal minutesFromBase, int baseHour = 12)
    {
        var total = ((int)Math.Round(minutesFromBase, MidpointRounding.AwayFromZero) % MinutesPerDay + MinutesPerDay) % MinutesPerDay;
        var hour = (total / 60 + baseHour) % 24;
        var minute = total % 60;
        return $"{hour:00}:{minute:00}";
    }
}
