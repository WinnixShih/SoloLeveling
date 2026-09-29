using SoloLeveling.Domain.Entities;

namespace SoloLeveling.Domain.Rules;

/// <summary>
/// 「今日」進度的更新規則：寫入進度值、重算達標率與達標獎勵、清除單一任務進度。
/// 所有方法只修改傳入的 <see cref="Player"/> 與 <see cref="DailyLog"/>，並回傳這次產生的 <see cref="XpEvent"/>，交由呼叫端持久化。
/// </summary>
public static class ProgressUpdater
{
    /// <summary>
    /// 對某任務寫入新的進度值（規格 4.4）。漸進任務以當階目標判定（見 <see cref="Progression.EffectiveTarget"/>），並把判定用的目標寫入 <see cref="QuestProgress.TargetSnapshot"/>。
    /// </summary>
    /// <param name="player">玩家。</param>
    /// <param name="log">今日紀錄（含 <see cref="DailyLog.Progresses"/>）。</param>
    /// <param name="activeQuests">今日未封存的任務，用來算達標率分母。</param>
    /// <param name="quest">要寫入的任務。</param>
    /// <param name="value">新的進度值；null 表示未填。</param>
    /// <param name="now">當下時間（UTC）。</param>
    /// <param name="doneDaysBeforeToday">此任務在今天之前的達標天數；一般任務傳 0 即可。</param>
    /// <returns>這次產生的 EXP 事件。</returns>
    /// <exception cref="DomainValidationException">Check 類型的值不是 0／1，或 Count／Limit 的值為負。</exception>
    public static IReadOnlyList<XpEvent> SetValue(
        Player player,
        DailyLog log,
        IReadOnlyList<Quest> activeQuests,
        Quest quest,
        decimal? value,
        DateTimeOffset now,
        int doneDaysBeforeToday)
    {
        ValidateValue(quest, value);

        var events = new List<XpEvent>();
        var progress = log.Progresses.FirstOrDefault(p => p.QuestId == quest.Id);
        if (progress == null)
        {
            progress = new QuestProgress { Id = Guid.NewGuid(), DailyLogId = log.Id, QuestId = quest.Id, CreatedAt = now };
            log.Progresses.Add(progress);
        }

        var target = Progression.EffectiveTarget(quest, doneDaysBeforeToday);
        var wasDone = progress.IsDone;
        var isDone = CompletionRules.IsDone(quest.QuestType, value, target);
        progress.Value = value;
        progress.IsDone = isDone;
        progress.TargetSnapshot = target;

        if (!wasDone && isDone)
        {
            events.Add(Grant(player, progress, quest, now));
        }
        else if (wasDone && !isDone)
        {
            events.Add(Revoke(player, progress, quest, now));
        }

        events.AddRange(Recalculate(player, log, activeQuests, now));
        return events;
    }

    /// <summary>
    /// 重算今日達標率與達標狀態，並補發或收回達標獎勵（規格 4.5）；切換模式、封存任務後呼叫。
    /// </summary>
    /// <param name="player">玩家。</param>
    /// <param name="log">今日紀錄。</param>
    /// <param name="activeQuests">今日未封存的任務。</param>
    /// <param name="now">當下時間（UTC）。</param>
    /// <returns>這次產生的 EXP 事件。</returns>
    public static IReadOnlyList<XpEvent> Recalculate(Player player, DailyLog log, IReadOnlyList<Quest> activeQuests, DateTimeOffset now)
    {
        var events = new List<XpEvent>();
        var activeIds = activeQuests.Select(q => q.Id).ToHashSet();
        var doneCount = log.Progresses.Count(p => p.IsDone && activeIds.Contains(p.QuestId));

        log.CompletionRatio = CompletionRules.CompletionRatio(doneCount, activeQuests.Count);
        log.IsCleared = log.CompletionRatio >= CompletionRules.ThresholdOf(player.HardMode);

        if (log.IsCleared && !log.BonusGranted)
        {
            Leveling.GainXp(player, CompletionRules.DailyBonusXp);
            log.BonusGranted = true;
            events.Add(NewEvent(player.UserId, XpSource.DailyBonus, CompletionRules.DailyBonusXp, log.Id, now));
        }
        else if (!log.IsCleared && log.BonusGranted)
        {
            Leveling.LoseXp(player, CompletionRules.DailyBonusXp);
            log.BonusGranted = false;
            events.Add(NewEvent(player.UserId, XpSource.DailyBonusUndo, -CompletionRules.DailyBonusXp, log.Id, now));
        }

        var displayStreak = player.Streak + (log.IsCleared ? 1 : 0);
        player.BestStreak = Math.Max(player.BestStreak, displayStreak);
        return events;
    }

    /// <summary>
    /// 移除某任務今日的進度（改任務類型時用），已發的 EXP 與屬性一併撤銷，之後重算達標（規格 4.9）。
    /// </summary>
    /// <param name="player">玩家。</param>
    /// <param name="log">今日紀錄。</param>
    /// <param name="activeQuests">今日未封存的任務。</param>
    /// <param name="quest">要清除進度的任務。</param>
    /// <param name="now">當下時間（UTC）。</param>
    /// <returns>這次產生的 EXP 事件。</returns>
    public static IReadOnlyList<XpEvent> ClearQuestProgress(Player player, DailyLog log, IReadOnlyList<Quest> activeQuests, Quest quest, DateTimeOffset now)
    {
        var events = new List<XpEvent>();
        var progress = log.Progresses.FirstOrDefault(p => p.QuestId == quest.Id);
        if (progress != null)
        {
            if (progress.IsDone)
            {
                events.Add(Revoke(player, progress, quest, now));
            }

            log.Progresses.Remove(progress);
        }

        events.AddRange(Recalculate(player, log, activeQuests, now));
        return events;
    }

    private static void ValidateValue(Quest quest, decimal? value)
    {
        if (quest.QuestType == QuestType.Check && value is not (null or 0 or 1))
        {
            throw new DomainValidationException("InvalidValue", "Check 類型的值只能是 0 或 1");
        }

        if (quest.QuestType != QuestType.Check && value < 0)
        {
            throw new DomainValidationException("InvalidValue", "進度值不可為負");
        }
    }

    private static XpEvent Grant(Player player, QuestProgress progress, Quest quest, DateTimeOffset now)
    {
        var reward = CompletionRules.RewardOf(quest.Difficulty);
        Leveling.GainXp(player, reward.Xp);
        player.AddStat(quest.StatType, reward.Stat);
        player.TotalCompleted += 1;
        progress.XpGranted = reward.Xp;
        progress.StatGranted = reward.Stat;
        return NewEvent(player.UserId, XpSource.Quest, reward.Xp, quest.Id, now);
    }

    private static XpEvent Revoke(Player player, QuestProgress progress, Quest quest, DateTimeOffset now)
    {
        var xp = progress.XpGranted;
        Leveling.LoseXp(player, xp);
        player.AddStat(quest.StatType, -progress.StatGranted);
        player.TotalCompleted = Math.Max(0, player.TotalCompleted - 1);
        progress.XpGranted = 0;
        progress.StatGranted = 0;
        return NewEvent(player.UserId, XpSource.QuestUndo, -xp, quest.Id, now);
    }

    private static XpEvent NewEvent(Guid userId, XpSource source, int amount, Guid refId, DateTimeOffset now)
    {
        return new XpEvent { Id = Guid.NewGuid(), UserId = userId, Source = source, Amount = amount, RefId = refId, OccurredAt = now };
    }
}
