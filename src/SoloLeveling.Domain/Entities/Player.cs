namespace SoloLeveling.Domain.Entities;

/// <summary>
/// 玩家的遊戲化狀態；與 <see cref="User"/> 一對一，以 UserId 為主鍵。
/// </summary>
public class Player
{
    /// <summary>主鍵，同時是 <see cref="User"/> 的外鍵。</summary>
    public Guid UserId { get; set; }

    /// <summary>目前等級，最小 1。</summary>
    public int Level { get; set; } = 1;

    /// <summary>目前等級內累積的 EXP，永遠 &gt;= 0。</summary>
    public int Xp { get; set; }

    /// <summary>力量。</summary>
    public int Str { get; set; } = 10;

    /// <summary>體力。</summary>
    public int Vit { get; set; } = 10;

    /// <summary>智力。</summary>
    public int Int { get; set; } = 10;

    /// <summary>意志。</summary>
    public int Wil { get; set; } = 10;

    /// <summary>精神。</summary>
    public int Spi { get; set; } = 10;

    /// <summary>是否啟用困難模式（達標門檻 100%、未達標會扣 EXP）。</summary>
    public bool HardMode { get; set; }

    /// <summary>已結算日子的連續達標天數；只在結算時變動，今日的即時值由 API 另外加上。</summary>
    public int Streak { get; set; }

    /// <summary>歷史最高連續達標天數（含今日即時值）。</summary>
    public int BestStreak { get; set; }

    /// <summary>累計完成任務次數；撤銷完成時會減回。</summary>
    public int TotalCompleted { get; set; }

    /// <summary>最後一個已結算的日期（使用者時區）；null 表示尚未結算過。</summary>
    public DateOnly? LastSettledDate { get; set; }

    /// <summary>建立時間（UTC）；首次結算的起算日由此換算。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 增減指定屬性；結果最低為 0。
    /// </summary>
    /// <param name="stat">屬性。</param>
    /// <param name="delta">增減量，可為負。</param>
    public void AddStat(StatType stat, int delta)
    {
        switch (stat)
        {
            case StatType.Strength:
                Str = Math.Max(0, Str + delta);
                break;
            case StatType.Vitality:
                Vit = Math.Max(0, Vit + delta);
                break;
            case StatType.Intelligence:
                Int = Math.Max(0, Int + delta);
                break;
            case StatType.Willpower:
                Wil = Math.Max(0, Wil + delta);
                break;
            case StatType.Spirit:
                Spi = Math.Max(0, Spi + delta);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stat), stat, "未知的屬性");
        }
    }
}
