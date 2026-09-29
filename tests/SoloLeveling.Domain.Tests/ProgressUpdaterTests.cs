using FluentAssertions;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class ProgressUpdaterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 1, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.NewGuid();

    private static Quest CheckQuest(StatType stat = StatType.Vitality, Difficulty difficulty = Difficulty.Normal)
    {
        return new Quest { Id = Guid.NewGuid(), UserId = UserId, QuestType = QuestType.Check, StatType = stat, Difficulty = difficulty };
    }

    private static (Player Player, DailyLog Log) NewDay(bool hardMode = false)
    {
        var player = new Player { UserId = UserId, HardMode = hardMode };
        var log = new DailyLog { Id = Guid.NewGuid(), UserId = UserId, Date = new DateOnly(2026, 9, 28) };
        return (player, log);
    }

    [Fact]
    public void SetValue_首次完成_發EXP與屬性並記錄在進度上()
    {
        var (player, log) = NewDay();
        var quest = CheckQuest(StatType.Vitality, Difficulty.Normal);
        var quests = new[] { quest, CheckQuest(), CheckQuest() };

        var events = ProgressUpdater.SetValue(player, log, quests, quest, 1, Now, 0);

        events.Should().ContainSingle(e => e.Source == XpSource.Quest && e.Amount == 20 && e.RefId == quest.Id);
        player.Xp.Should().Be(20);
        player.Vit.Should().Be(11);
        player.TotalCompleted.Should().Be(1);
        var progress = log.Progresses.Single(p => p.QuestId == quest.Id);
        progress.Value.Should().Be(1);
        progress.IsDone.Should().BeTrue();
        progress.XpGranted.Should().Be(20);
        progress.StatGranted.Should().Be(1);
    }

    [Fact]
    public void SetValue_完成後撤銷_EXP與屬性淨變化為0()
    {
        var (player, log) = NewDay();
        var quest = CheckQuest(StatType.Strength, Difficulty.Hard);
        var quests = new[] { quest, CheckQuest(), CheckQuest() };

        ProgressUpdater.SetValue(player, log, quests, quest, 1, Now, 0);
        var events = ProgressUpdater.SetValue(player, log, quests, quest, 0, Now, 0);

        events.Should().ContainSingle(e => e.Source == XpSource.QuestUndo && e.Amount == -35);
        player.Xp.Should().Be(0);
        player.Level.Should().Be(1);
        player.Str.Should().Be(10);
        player.TotalCompleted.Should().Be(0);
        var progress = log.Progresses.Single(p => p.QuestId == quest.Id);
        progress.IsDone.Should().BeFalse();
        progress.XpGranted.Should().Be(0);
        progress.StatGranted.Should().Be(0);
    }

    [Fact]
    public void SetValue_已完成再寫入仍完成的值_不重複發獎勵()
    {
        var (player, log) = NewDay();
        var quest = new Quest { Id = Guid.NewGuid(), UserId = UserId, QuestType = QuestType.Count, TargetValue = 8, Difficulty = Difficulty.Easy, StatType = StatType.Vitality };
        var quests = new[] { quest, CheckQuest(), CheckQuest() };

        ProgressUpdater.SetValue(player, log, quests, quest, 8, Now, 0);
        var events = ProgressUpdater.SetValue(player, log, quests, quest, 9, Now, 0);

        events.Should().BeEmpty();
        player.Xp.Should().Be(10);
        player.Vit.Should().Be(11);
    }

    [Fact]
    public void SetValue_一般模式達到70percent_首次發達標獎勵且只發一次()
    {
        var (player, log) = NewDay();
        var quests = Enumerable.Range(0, 10).Select(_ => CheckQuest(StatType.Spirit, Difficulty.Easy)).ToArray();

        for (var i = 0; i < 6; i++)
        {
            ProgressUpdater.SetValue(player, log, quests, quests[i], 1, Now, 0);
        }

        log.IsCleared.Should().BeFalse();
        log.BonusGranted.Should().BeFalse();

        var seventh = ProgressUpdater.SetValue(player, log, quests, quests[6], 1, Now, 0);

        seventh.Should().Contain(e => e.Source == XpSource.DailyBonus && e.Amount == 30 && e.RefId == log.Id);
        log.IsCleared.Should().BeTrue();
        log.BonusGranted.Should().BeTrue();
        log.CompletionRatio.Should().Be(0.7m);

        var eighth = ProgressUpdater.SetValue(player, log, quests, quests[7], 1, Now, 0);

        eighth.Should().NotContain(e => e.Source == XpSource.DailyBonus);
        // 8 * 10 + 30 = 110，超過 Lv1 所需 100 → Lv2 剩 10
        player.Level.Should().Be(2);
        player.Xp.Should().Be(10);
    }

    [Fact]
    public void SetValue_達標後撤銷一個任務_收回達標獎勵()
    {
        var (player, log) = NewDay();
        var quests = Enumerable.Range(0, 10).Select(_ => CheckQuest(StatType.Spirit, Difficulty.Easy)).ToArray();
        for (var i = 0; i < 7; i++)
        {
            ProgressUpdater.SetValue(player, log, quests, quests[i], 1, Now, 0);
        }

        var events = ProgressUpdater.SetValue(player, log, quests, quests[0], 0, Now, 0);

        events.Should().Contain(e => e.Source == XpSource.DailyBonusUndo && e.Amount == -30);
        log.IsCleared.Should().BeFalse();
        log.BonusGranted.Should().BeFalse();
        player.Xp.Should().Be(60);
    }

    [Fact]
    public void Recalculate_切到困難模式_同一天變未達標並收回獎勵()
    {
        var (player, log) = NewDay();
        var quests = Enumerable.Range(0, 10).Select(_ => CheckQuest(StatType.Spirit, Difficulty.Easy)).ToArray();
        for (var i = 0; i < 7; i++)
        {
            ProgressUpdater.SetValue(player, log, quests, quests[i], 1, Now, 0);
        }

        player.HardMode = true;
        var events = ProgressUpdater.Recalculate(player, log, quests, Now);

        events.Should().ContainSingle(e => e.Source == XpSource.DailyBonusUndo && e.Amount == -30);
        log.IsCleared.Should().BeFalse();
        log.BonusGranted.Should().BeFalse();
        player.Xp.Should().Be(70);
    }

    [Fact]
    public void Recalculate_封存任務後分母變小_補發達標獎勵()
    {
        var (player, log) = NewDay();
        var quests = Enumerable.Range(0, 10).Select(_ => CheckQuest(StatType.Spirit, Difficulty.Easy)).ToList();
        for (var i = 0; i < 6; i++)
        {
            ProgressUpdater.SetValue(player, log, quests, quests[i], 1, Now, 0);
        }

        var remaining = quests.Take(8).ToArray();
        var events = ProgressUpdater.Recalculate(player, log, remaining, Now);

        events.Should().ContainSingle(e => e.Source == XpSource.DailyBonus && e.Amount == 30);
        log.CompletionRatio.Should().Be(0.75m);
        log.IsCleared.Should().BeTrue();
    }

    [Fact]
    public void SetValue_今日達標時_BestStreak以Streak加1維護()
    {
        var (player, log) = NewDay();
        player.Streak = 3;
        player.BestStreak = 3;
        var quest = CheckQuest();

        ProgressUpdater.SetValue(player, log, [quest], quest, 1, Now, 0);

        player.BestStreak.Should().Be(4);
        player.Streak.Should().Be(3);
    }

    [Fact]
    public void ClearQuestProgress_移除該任務今日進度並撤銷已發獎勵()
    {
        var (player, log) = NewDay();
        var quest = CheckQuest(StatType.Intelligence, Difficulty.Normal);
        var quests = new[] { quest };
        ProgressUpdater.SetValue(player, log, quests, quest, 1, Now, 0);

        var events = ProgressUpdater.ClearQuestProgress(player, log, quests, quest, Now);

        events.Should().Contain(e => e.Source == XpSource.QuestUndo && e.Amount == -20);
        events.Should().Contain(e => e.Source == XpSource.DailyBonusUndo);
        log.Progresses.Should().NotContain(p => p.QuestId == quest.Id);
        player.Xp.Should().Be(0);
        player.Int.Should().Be(10);
        log.IsCleared.Should().BeFalse();
    }

    [Theory]
    [InlineData(QuestType.Check, 2.0)]
    [InlineData(QuestType.Check, 0.5)]
    [InlineData(QuestType.Count, -1.0)]
    public void SetValue_不合法的值_擲出DomainValidationException(QuestType type, double value)
    {
        var (player, log) = NewDay();
        var quest = new Quest { Id = Guid.NewGuid(), UserId = UserId, QuestType = type, TargetValue = 5, Difficulty = Difficulty.Easy };

        var act = () => ProgressUpdater.SetValue(player, log, [quest], quest, (decimal)value, Now, 0);

        act.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void SetValue_Count漸進任務_以當階目標判定並寫入快照()
    {
        var player = new Player { UserId = Guid.NewGuid(), Level = 1, Xp = 0 };
        var log = new DailyLog { Id = Guid.NewGuid(), UserId = player.UserId, Date = new DateOnly(2026, 9, 28) };
        var quest = new Quest
        {
            Id = Guid.NewGuid(),
            UserId = player.UserId,
            Name = "閱讀",
            QuestType = QuestType.Count,
            Difficulty = Difficulty.Normal,
            StatType = StatType.Intelligence,
            GoalId = Guid.NewGuid(),
            ValueKind = ProgressionValueKind.Number,
            StartValue = 10,
            EndValue = 30,
            StepValue = 2,
            StageCount = 10,
            DaysPerStep = 3,
        };
        var now = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);

        // 第 1 階目標 12：填 11 未完成
        ProgressUpdater.SetValue(player, log, [quest], quest, 11, now, doneDaysBeforeToday: 0);
        var progress = log.Progresses.Single();
        progress.IsDone.Should().BeFalse();
        progress.TargetSnapshot.Should().Be(12);

        // 填 12 完成；activeQuests 只有這一個任務，達標率 100% 同時觸發當日達標獎勵 30
        ProgressUpdater.SetValue(player, log, [quest], quest, 12, now, doneDaysBeforeToday: 0);
        progress.IsDone.Should().BeTrue();
        player.Xp.Should().Be(50);

        // 已達標 3 天 → 第 2 階目標 14：12 不再算完成
        var log2 = new DailyLog { Id = Guid.NewGuid(), UserId = player.UserId, Date = new DateOnly(2026, 10, 1) };
        ProgressUpdater.SetValue(player, log2, [quest], quest, 12, now, doneDaysBeforeToday: 3);
        log2.Progresses.Single().IsDone.Should().BeFalse();
        log2.Progresses.Single().TargetSnapshot.Should().Be(14);
    }

    [Fact]
    public void SetValue_一般任務_快照等於TargetValue()
    {
        var player = new Player { UserId = Guid.NewGuid(), Level = 1, Xp = 0 };
        var log = new DailyLog { Id = Guid.NewGuid(), UserId = player.UserId, Date = new DateOnly(2026, 9, 28) };
        var quest = new Quest { Id = Guid.NewGuid(), UserId = player.UserId, Name = "喝水", QuestType = QuestType.Count, Difficulty = Difficulty.Easy, StatType = StatType.Vitality, TargetValue = 8 };
        var now = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);

        ProgressUpdater.SetValue(player, log, [quest], quest, 8, now, doneDaysBeforeToday: 0);

        log.Progresses.Single().TargetSnapshot.Should().Be(8);
    }
}
