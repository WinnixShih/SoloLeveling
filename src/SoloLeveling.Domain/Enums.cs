namespace SoloLeveling.Domain;

/// <summary>
/// 五維屬性；每個任務歸屬一種屬性，完成任務時加該屬性點數。
/// </summary>
public enum StatType
{
    /// <summary>力量（STR）。</summary>
    Strength = 1,

    /// <summary>體力（VIT）。</summary>
    Vitality = 2,

    /// <summary>智力（INT）。</summary>
    Intelligence = 3,

    /// <summary>意志（WIL）。</summary>
    Willpower = 4,

    /// <summary>精神（SPI）。</summary>
    Spirit = 5,
}

/// <summary>
/// 任務難度；決定完成時的 EXP 與屬性獎勵。
/// </summary>
public enum Difficulty
{
    /// <summary>簡單：+10 EXP、+1 屬性。</summary>
    Easy = 1,

    /// <summary>普通：+20 EXP、+1 屬性。</summary>
    Normal = 2,

    /// <summary>困難：+35 EXP、+2 屬性。</summary>
    Hard = 3,
}

/// <summary>
/// 任務類型；決定 <c>Value</c> 的意義與完成判定方式。
/// </summary>
public enum QuestType
{
    /// <summary>勾選型：value 為 0 或 1，value &gt;= 1 即完成。</summary>
    Check = 1,

    /// <summary>累計型：value 為累計量，value &gt;= TargetValue 即完成。</summary>
    Count = 2,

    /// <summary>上限型：value 為實際量（null 表示未填），value != null &amp;&amp; value &lt;= TargetValue 即完成。</summary>
    Limit = 3,
}

/// <summary>
/// EXP 流水的來源。
/// </summary>
public enum XpSource
{
    /// <summary>完成任務。</summary>
    Quest = 1,

    /// <summary>撤銷已完成的任務。</summary>
    QuestUndo = 2,

    /// <summary>當日達標獎勵。</summary>
    DailyBonus = 3,

    /// <summary>收回當日達標獎勵。</summary>
    DailyBonusUndo = 4,

    /// <summary>困難模式未達標懲罰。</summary>
    Penalty = 5,
}

/// <summary>
/// 引導式目標的類別；每個類別有自己的問題與漸進公式（見 <c>GoalCategories</c>）。
/// </summary>
public enum GoalCategory
{
    /// <summary>作息：就寢與起床時間。</summary>
    Routine = 1,

    /// <summary>運動：每天分鐘數。</summary>
    Exercise = 2,

    /// <summary>閱讀：每天分鐘數。</summary>
    Reading = 3,

    /// <summary>螢幕時間：每天小時上限。</summary>
    ScreenTime = 4,
}

/// <summary>
/// 漸進任務的目標值種類，決定顯示與四捨五入方式。
/// </summary>
public enum ProgressionValueKind
{
    /// <summary>一般數字（分鐘、小時等）。</summary>
    Number = 1,

    /// <summary>一天中的時間點，存成距中午 12:00 的分鐘數（見 <c>TimeOfDay</c>）。</summary>
    TimeOfDay = 2,

    /// <summary>一天中的時間點，以 18:00 為基準，供起床時間使用（見 <c>TimeOfDay</c>）。</summary>
    TimeOfDayEvening = 3,
}

/// <summary>
/// 卡片稀有度，也是寶箱等級；兩者一對一（E 級寶箱只開出 E 級卡）。
/// </summary>
public enum Rarity
{
    /// <summary>E 級。</summary>
    E = 1,

    /// <summary>C 級。</summary>
    C = 2,

    /// <summary>A 級。</summary>
    A = 3,

    /// <summary>S 級。</summary>
    S = 4,
}

/// <summary>
/// 寶箱的取得來源。
/// </summary>
public enum ChestSource
{
    /// <summary>升 1 級（E）。</summary>
    LevelUp = 1,

    /// <summary>階級晉升（C）。</summary>
    RankUp = 2,

    /// <summary>最佳連續首次達 7 天（C）。</summary>
    Streak7 = 3,

    /// <summary>最佳連續首次達 30 天（A）。</summary>
    Streak30 = 4,

    /// <summary>目標完成（A）。</summary>
    GoalCompleted = 5,

    /// <summary>66 天週期完成（S）。</summary>
    ProgramCompleted = 6,

    /// <summary>商店購買（E）。</summary>
    Purchase = 7,
}

/// <summary>
/// 金幣流水的來源。
/// </summary>
public enum CoinSource
{
    /// <summary>今日首次達標。</summary>
    DailyClear = 1,

    /// <summary>收回今日達標金幣。</summary>
    DailyClearUndo = 2,

    /// <summary>開箱固定金幣。</summary>
    ChestOpen = 3,

    /// <summary>重複卡轉換。</summary>
    DuplicateCard = 4,

    /// <summary>解鎖成就。</summary>
    Achievement = 5,

    /// <summary>購買連勝保險卡。</summary>
    ShopShield = 6,

    /// <summary>購買 E 級寶箱。</summary>
    ShopChest = 7,

    /// <summary>購買主題。</summary>
    ShopTheme = 8,
}

/// <summary>
/// 獎勵相關事件的種類。
/// </summary>
public enum RewardEventKind
{
    /// <summary>結算時自動消耗 1 張連勝保險卡。</summary>
    ShieldUsed = 1,
}

/// <summary>
/// 稱號字塊的位置。
/// </summary>
public enum TitleSlot
{
    /// <summary>前綴（例：靜夜的）。</summary>
    Prefix = 1,

    /// <summary>後綴（例：百戰獵人）。</summary>
    Suffix = 2,
}

/// <summary>
/// 由漸進任務推導出的角色，供分類成就使用；一般任務沒有角色。
/// </summary>
public enum QuestRole
{
    /// <summary>就寢（作息、<see cref="ProgressionValueKind.TimeOfDay"/>）。</summary>
    Bedtime = 1,

    /// <summary>起床（作息、<see cref="ProgressionValueKind.TimeOfDayEvening"/>）。</summary>
    WakeUp = 2,

    /// <summary>運動。</summary>
    Exercise = 3,

    /// <summary>閱讀。</summary>
    Reading = 4,

    /// <summary>螢幕時間。</summary>
    ScreenTime = 5,
}

/// <summary>
/// 商店商品。
/// </summary>
public enum ShopItem
{
    /// <summary>連勝保險卡。</summary>
    Shield = 1,

    /// <summary>E 級寶箱。</summary>
    EChest = 2,

    /// <summary>主題色（需指定 themeKey）。</summary>
    Theme = 3,
}
