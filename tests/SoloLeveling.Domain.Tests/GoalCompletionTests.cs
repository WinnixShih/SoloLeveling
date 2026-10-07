using FluentAssertions;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class GoalCompletionTests
{
    private static Quest Staged(int stageCount, int daysPerStep)
    {
        return new Quest { Id = Guid.NewGuid(), GoalId = Guid.NewGuid(), StageCount = stageCount, DaysPerStep = daysPerStep };
    }

    [Theory]
    [InlineData(3, 3, 7)]
    [InlineData(10, 3, 28)]
    [InlineData(8, 4, 29)]
    [InlineData(1, 3, 1)]
    public void DoneDaysToFinish_在最後一階達標一天(int stageCount, int daysPerStep, int expected)
    {
        GoalCompletion.DoneDaysToFinish(Staged(stageCount, daysPerStep)).Should().Be(expected);
    }

    private static readonly DateOnly Start = new(2026, 9, 28);

    private static Goal GoalOf(int lengthDays)
    {
        return new Goal { Id = Guid.NewGuid(), StartDate = Start, LengthDays = lengthDays };
    }

    [Fact]
    public void IsFinished_一階目標第一天達標_期間未走完不算完成()
    {
        var quest = Staged(1, 3);

        GoalCompletion.IsFinished(GoalOf(7), Start, [quest], _ => 1).Should().BeFalse();
        GoalCompletion.IsFinished(GoalOf(7), Start.AddDays(6), [quest], _ => 1).Should().BeTrue();
    }

    [Fact]
    public void IsFinished_每個任務都在最後一階達標過才算完成()
    {
        var reading = Staged(3, 3);
        var wake = Staged(1, 3);
        var days = new Dictionary<Guid, int> { [reading.Id] = 7, [wake.Id] = 0 };

        GoalCompletion.IsFinished(GoalOf(7), Start.AddDays(6), [reading, wake], q => days[q.Id]).Should().BeFalse();

        days[wake.Id] = 1;
        GoalCompletion.IsFinished(GoalOf(7), Start.AddDays(6), [reading, wake], q => days[q.Id]).Should().BeTrue();
    }

    [Fact]
    public void IsFinished_只有一階_建立當下不算完成()
    {
        GoalCompletion.IsFinished(GoalOf(7), Start.AddDays(6), [Staged(1, 3)], _ => 0).Should().BeFalse();
    }

    [Fact]
    public void IsFinished_沒有未封存任務_不算完成()
    {
        GoalCompletion.IsFinished(GoalOf(7), Start.AddDays(6), [], _ => 100).Should().BeFalse();
    }
}
