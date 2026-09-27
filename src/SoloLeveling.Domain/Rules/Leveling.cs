using SoloLeveling.Domain.Entities;

namespace SoloLeveling.Domain.Rules;

/// <summary>
/// 階級與稱號。
/// </summary>
/// <param name="Rank">階級代碼（E–S）。</param>
/// <param name="Title">稱號。</param>
public record PlayerRank(string Rank, string Title);

/// <summary>
/// 等級與 EXP 規則。所有方法直接修改 <see cref="Player"/>；呼叫端須自行寫入對應的 <see cref="XpEvent"/>。
/// </summary>
public static class Leveling
{
    /// <summary>
    /// 升到下一級所需 EXP：<c>100 + 20 * (level - 1)</c>。
    /// </summary>
    /// <param name="level">目前等級。</param>
    /// <returns>該等級升級所需 EXP。</returns>
    public static int XpNeeded(int level)
    {
        return 100 + (20 * (level - 1));
    }

    /// <summary>
    /// 加 EXP，溢出時連續升級。
    /// </summary>
    /// <param name="player">玩家。</param>
    /// <param name="amount">EXP 量（正值）。</param>
    public static void GainXp(Player player, int amount)
    {
        player.Xp += amount;
        while (player.Xp >= XpNeeded(player.Level))
        {
            player.Xp -= XpNeeded(player.Level);
            player.Level += 1;
        }
    }

    /// <summary>
    /// 扣 EXP（撤銷完成用），不足時降級並補回上一級的所需值；Lv1 時最低為 0。
    /// </summary>
    /// <param name="player">玩家。</param>
    /// <param name="amount">EXP 量（正值）。</param>
    public static void LoseXp(Player player, int amount)
    {
        player.Xp -= amount;
        while (player.Xp < 0 && player.Level > 1)
        {
            player.Level -= 1;
            player.Xp += XpNeeded(player.Level);
        }

        if (player.Xp < 0)
        {
            player.Xp = 0;
        }
    }

    /// <summary>
    /// 懲罰扣 EXP：不降級，最低為 0。
    /// </summary>
    /// <param name="player">玩家。</param>
    /// <param name="amount">EXP 量（正值）。</param>
    public static void ApplyPenalty(Player player, int amount)
    {
        player.Xp = Math.Max(0, player.Xp - amount);
    }

    /// <summary>
    /// 依等級取得階級與稱號。
    /// </summary>
    /// <param name="level">等級。</param>
    /// <returns>階級與稱號。</returns>
    public static PlayerRank RankOf(int level)
    {
        return level switch
        {
            < 5 => new PlayerRank("E", "新手"),
            < 10 => new PlayerRank("D", "見習者"),
            < 20 => new PlayerRank("C", "挑戰者"),
            < 30 => new PlayerRank("B", "精英"),
            < 45 => new PlayerRank("A", "大師"),
            _ => new PlayerRank("S", "傳說"),
        };
    }
}
