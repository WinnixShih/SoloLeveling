using FluentAssertions;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class QuestRolesTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    [Theory]
    [InlineData(GoalCategory.Routine, ProgressionValueKind.TimeOfDay, QuestRole.Bedtime)]
    [InlineData(GoalCategory.Routine, ProgressionValueKind.TimeOfDayEvening, QuestRole.WakeUp)]
    [InlineData(GoalCategory.Exercise, ProgressionValueKind.Number, QuestRole.Exercise)]
    [InlineData(GoalCategory.Reading, ProgressionValueKind.Number, QuestRole.Reading)]
    [InlineData(GoalCategory.ScreenTime, ProgressionValueKind.Number, QuestRole.ScreenTime)]
    public void Of_由類別與值種類推導角色(GoalCategory category, ProgressionValueKind kind, QuestRole expected)
    {
        QuestRoles.Of(category, kind).Should().Be(expected);
    }

    [Fact]
    public void Of_作息但值種類不是時間_沒有角色()
    {
        QuestRoles.Of(GoalCategory.Routine, ProgressionValueKind.Number).Should().BeNull();
    }

    [Fact]
    public void StreakOf_今天已完成_連同前兩天算3()
    {
        QuestRoles.StreakOf(new HashSet<DateOnly> { Today, Today.AddDays(-1), Today.AddDays(-2) }, Today).Should().Be(3);
    }

    [Fact]
    public void StreakOf_今天未完成_算到昨天為止()
    {
        QuestRoles.StreakOf(new HashSet<DateOnly> { Today.AddDays(-1), Today.AddDays(-2) }, Today).Should().Be(2);
    }

    [Fact]
    public void StreakOf_中間斷一天就停()
    {
        QuestRoles.StreakOf(new HashSet<DateOnly> { Today, Today.AddDays(-2), Today.AddDays(-3) }, Today).Should().Be(1);
        QuestRoles.StreakOf(new HashSet<DateOnly>(), Today).Should().Be(0);
    }

    [Theory]
    [InlineData(QuestRole.Exercise, true)]
    [InlineData(QuestRole.Reading, true)]
    [InlineData(QuestRole.Bedtime, false)]
    [InlineData(QuestRole.WakeUp, false)]
    [InlineData(QuestRole.ScreenTime, false)]
    public void CountsMinutes_只有運動與閱讀累計分鐘(QuestRole role, bool expected)
    {
        QuestRoles.CountsMinutes(role).Should().Be(expected);
    }
}
