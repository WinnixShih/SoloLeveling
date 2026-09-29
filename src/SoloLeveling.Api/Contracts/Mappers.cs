using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;
using ProgramEntity = SoloLeveling.Domain.Entities.Program;

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

    /// <summary>玩家狀態；displayStreak 依今日是否達標即時加 1。</summary>
    /// <param name="player">玩家實體。</param>
    /// <param name="todayCleared">今日是否達標。</param>
    /// <returns>DTO。</returns>
    public static PlayerDto ToDto(this Player player, bool todayCleared)
    {
        var rank = Leveling.RankOf(player.Level);
        return new PlayerDto(
            player.Level,
            player.Xp,
            Leveling.XpNeeded(player.Level),
            rank.Rank,
            rank.Title,
            new StatsDto(player.Str, player.Vit, player.Int, player.Wil, player.Spi),
            player.HardMode,
            player.Streak + (todayCleared ? 1 : 0),
            player.BestStreak,
            player.TotalCompleted);
    }

    /// <summary>66 天計畫；dayNumber 以今日計算。</summary>
    /// <param name="program">計畫實體。</param>
    /// <param name="today">使用者時區的今日。</param>
    /// <returns>DTO。</returns>
    public static ProgramDto ToDto(this ProgramEntity program, DateOnly today)
    {
        var dayNumber = today.DayNumber - program.StartDate.DayNumber + 1;
        return new ProgramDto(program.StartDate, program.Cycle, dayNumber, program.LengthDays, dayNumber > program.LengthDays);
    }

    /// <summary>時間戳轉 Unix 秒。</summary>
    /// <param name="value">UTC 時間。</param>
    /// <returns>Unix 秒。</returns>
    public static long ToUnixSeconds(this DateTimeOffset value)
    {
        return value.ToUnixTimeSeconds();
    }
}
