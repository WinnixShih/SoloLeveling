namespace SoloLeveling.Domain;

/// <summary>
/// 介面主題鍵；對應前端 <c>&lt;html data-accent&gt;</c>。
/// </summary>
public static class Themes
{
    /// <summary>預設主題（藍光），免費、不需購買。</summary>
    public const string Default = "azure";

    /// <summary>全部主題：藍光、紫影、翡翠。</summary>
    public static IReadOnlyList<string> All { get; } = [Default, "violet", "jade"];

    /// <summary>
    /// 主題鍵是否存在。
    /// </summary>
    /// <param name="key">主題鍵。</param>
    /// <returns>是否為已知主題。</returns>
    public static bool Exists(string key)
    {
        return All.Contains(key);
    }
}
