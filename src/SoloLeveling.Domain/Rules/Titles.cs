namespace SoloLeveling.Domain.Rules;

/// <summary>
/// 稱號組合規則：有選字塊 → 「前綴・後綴」（只選一邊就只顯示該邊）；都沒選 → 階級稱號。
/// </summary>
public static class Titles
{
    /// <summary>前綴與後綴之間的分隔字。</summary>
    public const string Separator = "・";

    /// <summary>
    /// 組出顯示用稱號。
    /// </summary>
    /// <param name="prefixKey">前綴字塊鍵；null 表示不選。</param>
    /// <param name="suffixKey">後綴字塊鍵；null 表示不選。</param>
    /// <param name="level">目前等級，都沒選時取階級稱號。</param>
    /// <returns>顯示用稱號。</returns>
    public static string Compose(string? prefixKey, string? suffixKey, int level)
    {
        var parts = new[] { prefixKey, suffixKey }
            .Where(k => k is not null)
            .Select(k => Achievements.Find(k!)?.TitleText)
            .OfType<string>()
            .ToList();
        return parts.Count == 0 ? Leveling.RankOf(level).Title : string.Join(Separator, parts);
    }
}
