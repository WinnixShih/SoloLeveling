namespace SoloLeveling.Domain;

/// <summary>
/// 卡片目錄中的一張卡。
/// </summary>
/// <param name="Id">kebab-case 識別碼，也是插畫檔名。</param>
/// <param name="Name">名稱。</param>
/// <param name="Rarity">稀有度。</param>
/// <param name="Flavor">一兩句風味文字。</param>
public sealed record CardDefinition(string Id, string Name, Rarity Rarity, string Flavor)
{
    /// <summary>插畫相對路徑（<c>cards/{Id}.webp</c>）；檔案不存在時前端顯示佔位卡。</summary>
    public string Image => $"cards/{Id}.webp";
}

/// <summary>
/// 全部卡片。順序即圖鑑顯示順序；同稀有度內的順序也是抽卡時的索引順序。
/// </summary>
public static class Cards
{
    /// <summary>全部卡片：E 10、C 8、A 5、S 2。</summary>
    public static IReadOnlyList<CardDefinition> All { get; } =
    [
        new("rusty-dagger", "生鏽的短劍", Rarity.E, "每個獵人的第一把武器。刀刃鈍了，握柄還記得你的手。"),
        new("healing-potion", "低階回復藥水", Rarity.E, "苦得要命。喝下去的那一刻，你決定明天還要再來。"),
        new("dungeon-key", "地下城鑰匙", Rarity.E, "E 級傳送門的鑰匙，冰冷又平凡，卻能打開第一扇門。"),
        new("leather-boots", "舊皮靴", Rarity.E, "鞋底磨平了，代表你走過的路比昨天多。"),
        new("system-window", "系統視窗", Rarity.E, "「每日任務已更新。」它從不催促，只是一直都在。"),
        new("goblin-mask", "哥布林面具", Rarity.E, "第一次討伐的戰利品，很醜，但值得留著。"),
        new("dungeon-torch", "地下城火把", Rarity.E, "火光只照得亮前方三步，剛好夠走下一步。"),
        new("mana-shard", "魔力結晶碎片", Rarity.E, "微弱的藍光，像清晨還沒完全醒來的天空。"),
        new("training-weights", "訓練負重", Rarity.E, "系統說：伏地挺身一百下。你說：好。"),
        new("hunter-license", "獵人證", Rarity.E, "照片拍得很差，但上面的名字是你的。"),
        new("knight-shield", "騎士之盾", Rarity.C, "盾面滿是刮痕，每一道都是沒有退後的證明。"),
        new("blue-gate", "藍色傳送門", Rarity.C, "門後的空氣比外面冷，心跳比平常快。"),
        new("iron-golem", "鐵之魔像", Rarity.C, "它不會累，也不會停。你開始懂它了。"),
        new("shadow-step", "影步", Rarity.C, "腳步聲消失的瞬間，你已經在下一個位置。"),
        new("focus-elixir", "專注藥劑", Rarity.C, "喝下後世界安靜了，只剩眼前這一件事。"),
        new("dungeon-map", "地下城地圖", Rarity.C, "地圖會自己補上你走過的路。"),
        new("shadow-wolves", "暗影狼群", Rarity.C, "牠們在黑暗裡跟著你，不是追殺，是同行。"),
        new("daily-chest", "每日寶箱", Rarity.C, "每天打開一次，裡面放的是昨天的你留下的東西。"),
        new("demon-castle", "惡魔城", Rarity.A, "一百層的高塔，第一層的門已經為你打開。"),
        new("shadow-knight", "影之騎士長", Rarity.A, "跪下的那一刻，它稱你為「主上」。"),
        new("red-gate", "紅色傳送門", Rarity.A, "進得去，出不來，除非你比昨天更強。"),
        new("double-dungeon", "雙重地下城", Rarity.A, "神像在微笑。那是一切的開始。"),
        new("twin-daggers", "君王的雙刃", Rarity.A, "輕得像沒有重量，鋒利得像決心。"),
        new("shadow-monarch", "影之君主", Rarity.S, "「起來吧。」黑暗回應了你的聲音。"),
        new("arise", "起來吧", Rarity.S, "所有被你擊倒過的懶惰與藉口，如今都站在你身後。"),
    ];

    private static readonly Dictionary<string, CardDefinition> Index = All.ToDictionary(c => c.Id);

    /// <summary>
    /// 依 ID 取卡片。
    /// </summary>
    /// <param name="id">卡片 ID。</param>
    /// <returns>卡片；不存在回 null。</returns>
    public static CardDefinition? Find(string id)
    {
        return Index.GetValueOrDefault(id);
    }

    /// <summary>
    /// 取某稀有度的全部卡片，依目錄順序。
    /// </summary>
    /// <param name="rarity">稀有度。</param>
    /// <returns>卡片清單。</returns>
    public static IReadOnlyList<CardDefinition> OfRarity(Rarity rarity)
    {
        return All.Where(c => c.Rarity == rarity).ToList();
    }
}
