using SoloLeveling.Domain;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Api.Contracts;

/// <summary>
/// 實體轉 DTO。時間戳在此轉成 Unix 秒（本專案唯一允許秒換算的地方）。
/// </summary>
public static class Mappers
{
    /// <summary>使用者。</summary>
    /// <param name="user">使用者實體。</param>
    /// <returns>DTO。</returns>
    public static UserDto ToDto(this User user)
    {
        return new UserDto(user.Id, user.Email, user.DisplayName, user.TimeZoneId);
    }

    /// <summary>任務。GET /quests 不結算、沒有達標天數，漸進時間類任務的名稱以終點渲染（見 <see cref="Progression.RenderFinalName"/>）。</summary>
    /// <param name="quest">任務實體。</param>
    /// <returns>DTO。</returns>
    public static QuestDto ToDto(this Quest quest)
    {
        return new QuestDto(quest.Id, Progression.RenderFinalName(quest), quest.StatType, quest.Difficulty, quest.QuestType, quest.TargetValue, quest.Step, quest.Unit, quest.SortOrder, quest.GoalId);
    }

    /// <summary>玩家狀態；displayStreak 依今日是否達標即時加 1，title 為組合後稱號（見 <see cref="Titles.Compose"/>）。</summary>
    /// <param name="player">玩家實體。</param>
    /// <param name="todayCleared">今日是否達標。</param>
    /// <returns>DTO。</returns>
    public static PlayerDto ToDto(this Player player, bool todayCleared)
    {
        var rank = Leveling.RankOf(player.Level);
        var pinned = player.PinnedCardId is { } cardId ? Cards.Find(cardId)?.ToDto() : null;
        return new PlayerDto(
            player.Level,
            player.Xp,
            Leveling.XpNeeded(player.Level),
            rank.Rank,
            Titles.Compose(player.TitlePrefixKey, player.TitleSuffixKey, player.Level),
            new StatsDto(player.Str, player.Vit, player.Int, player.Wil, player.Spi),
            player.HardMode,
            player.Streak + (todayCleared ? 1 : 0),
            player.BestStreak,
            player.TotalCompleted,
            rank.Title,
            player.Coins,
            player.ShieldCount,
            player.ThemeKey,
            pinned);
    }

    /// <summary>進行中的傳送門；dayNumber 以今日計算（見 <see cref="GateRules.DayNumber"/>）。</summary>
    /// <param name="gate">門。</param>
    /// <param name="today">使用者時區的今日。</param>
    /// <param name="misses">已漏天數。</param>
    /// <returns>DTO。</returns>
    public static ActiveGateDto ToDto(this Gate gate, DateOnly today, int misses)
    {
        return new ActiveGateDto(gate.Rank, gate.StartDate, GateRules.DayNumber(gate, today), gate.LengthDays, misses, gate.AllowedMisses, gate.Status);
    }

    /// <summary>等級表的一列；解鎖與通關次數依玩家的通關紀錄。</summary>
    /// <param name="gate">等級定義。</param>
    /// <param name="clearCounts">各等級通關次數。</param>
    /// <returns>DTO。</returns>
    /// <exception cref="InvalidOperationException">等級表的成就鍵不在成就目錄（目錄不一致的程式錯誤）。</exception>
    public static GateTierDto ToDto(this GateDefinition gate, IReadOnlyDictionary<GateRank, int> clearCounts)
    {
        var achievement = Achievements.Find(gate.AchievementKey)
            ?? throw new InvalidOperationException($"傳送門 {gate.Rank} 的成就鍵 {gate.AchievementKey} 不在成就目錄");
        return new GateTierDto(
            gate.Rank,
            gate.LengthDays,
            gate.AllowedMisses,
            gate.Chests.ToList(),
            achievement.Name,
            achievement.TitleText,
            gate.RequiredRank,
            Gates.IsUnlocked(gate.Rank, clearCounts),
            clearCounts.GetValueOrDefault(gate.Rank));
    }

    /// <summary>寶箱。</summary>
    /// <param name="chest">寶箱實體。</param>
    /// <returns>DTO。</returns>
    public static ChestDto ToDto(this RewardChest chest)
    {
        return new ChestDto(chest.Id, chest.Rarity, chest.Source, chest.CreatedAt.ToUnixSeconds());
    }

    /// <summary>卡片目錄項目。</summary>
    /// <param name="card">卡片定義。</param>
    /// <returns>DTO。</returns>
    public static CardDto ToDto(this CardDefinition card)
    {
        return new CardDto(card.Id, card.Name, card.Rarity, card.Flavor, card.Image);
    }

    /// <summary>新解鎖的成就。</summary>
    /// <param name="achievement">成就定義。</param>
    /// <returns>DTO。</returns>
    public static UnlockedAchievementDto ToUnlockedDto(this AchievementDefinition achievement)
    {
        return new UnlockedAchievementDto(achievement.Key, achievement.Name, achievement.TitleText, achievement.Slot);
    }

    /// <summary>時間戳轉 Unix 秒。</summary>
    /// <param name="value">UTC 時間。</param>
    /// <returns>Unix 秒。</returns>
    public static long ToUnixSeconds(this DateTimeOffset value)
    {
        return value.ToUnixTimeSeconds();
    }
}
