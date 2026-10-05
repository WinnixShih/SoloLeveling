namespace SoloLeveling.Domain;

/// <summary>
/// 商店價格與持有上限。
/// </summary>
public static class Shop
{
    /// <summary>連勝保險卡價格。</summary>
    public const int ShieldPrice = 100;

    /// <summary>E 級寶箱價格。</summary>
    public const int ChestPrice = 200;

    /// <summary>主題（紫影、翡翠）價格。</summary>
    public const int ThemePrice = 500;

    /// <summary>連勝保險卡最多持有張數；購買與晉階贈送都受此限制。</summary>
    public const int MaxShields = 3;
}
