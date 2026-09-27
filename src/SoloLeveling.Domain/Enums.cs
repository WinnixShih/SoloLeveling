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
