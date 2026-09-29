using FluentAssertions;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class ProgressionTests
{
    private static Quest Bedtime(decimal start, decimal end, int stageCount, decimal step)
    {
        return new Quest
        {
            Id = Guid.NewGuid(),
            Name = "{target} 前上床睡覺",
            QuestType = QuestType.Check,
            GoalId = Guid.NewGuid(),
            ValueKind = ProgressionValueKind.TimeOfDay,
            StartValue = start,
            EndValue = end,
            StepValue = step,
            StageCount = stageCount,
            DaysPerStep = 3,
        };
    }

    private static Quest WakeTime(decimal start, decimal end, int stageCount, decimal step)
    {
        return new Quest
        {
            Id = Guid.NewGuid(),
            Name = "{target} 前起床",
            QuestType = QuestType.Check,
            GoalId = Guid.NewGuid(),
            ValueKind = ProgressionValueKind.TimeOfDayEvening,
            StartValue = start,
            EndValue = end,
            StepValue = step,
            StageCount = stageCount,
            DaysPerStep = 3,
        };
    }

    private static Quest Reading(decimal start, decimal end, int stageCount, decimal step)
    {
        return new Quest
        {
            Id = Guid.NewGuid(),
            Name = "閱讀",
            QuestType = QuestType.Count,
            Unit = "分鐘",
            GoalId = Guid.NewGuid(),
            ValueKind = ProgressionValueKind.Number,
            StartValue = start,
            EndValue = end,
            StepValue = step,
            StageCount = stageCount,
            DaysPerStep = 3,
        };
    }

    [Theory]
    [InlineData(30, 3, 10)]
    [InlineData(7, 3, 3)]
    [InlineData(9, 3, 3)]
    [InlineData(1, 3, 1)]
    public void StageCountFor_天數除以每階天數無條件進位(int lengthDays, int daysPerStep, int expected)
    {
        Progression.StageCountFor(lengthDays, daysPerStep).Should().Be(expected);
    }

    [Theory]
    [InlineData(780, 720, 10, 5, -5)]   // 01:00→00:00，每階 -6 四捨五入到 5 分鐘 → -5
    [InlineData(780, 770, 10, 5, -5)]   // 差 10 分鐘切 10 階 → -1 四捨五入成 0，取一個 granularity
    [InlineData(10, 30, 10, 1, 2)]
    [InlineData(3, 2, 10, 0.25, -0.25)] // 小時類：-0.1 → -0.25
    [InlineData(720, 720, 1, 5, 0)]
    public void StepFor_依granularity四捨五入且不為零(decimal start, decimal end, int stageCount, decimal granularity, decimal expected)
    {
        Progression.StepFor(start, end, stageCount, granularity).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(5, 2)]
    [InlineData(6, 3)]
    [InlineData(27, 10)]
    [InlineData(100, 10)]
    public void StageOf_達標天數每3天升一階且不超過總階數(int doneDays, int expected)
    {
        Progression.StageOf(Bedtime(780, 720, 10, -5), doneDays).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 775)]   // 第 1 階 00:55
    [InlineData(3, 770)]   // 第 2 階 00:50
    [InlineData(26, 735)]  // 第 9 階 00:15
    [InlineData(27, 720)]  // 第 10 階＝終點 00:00
    [InlineData(99, 720)]
    public void EffectiveTarget_每階前進一步_最後一階等於終點(int doneDays, decimal expected)
    {
        Progression.EffectiveTarget(Bedtime(780, 720, 10, -5), doneDays).Should().Be(expected);
    }

    [Fact]
    public void EffectiveTarget_步進四捨五入後_不會越過終點()
    {
        // 01:00 → 00:50、10 階、每階 -5：第 2 階已到 770，第 3 階起必須停在 770
        var quest = Bedtime(780, 770, 10, -5);
        Progression.EffectiveTarget(quest, 6).Should().Be(770);
        Progression.EffectiveTarget(quest, 15).Should().Be(770);
    }

    [Fact]
    public void EffectiveTarget_起點等於終點_只有一階且等於終點()
    {
        Progression.EffectiveTarget(Bedtime(720, 720, 1, 0), 0).Should().Be(720);
        Progression.StageOf(Bedtime(720, 720, 1, 0), 30).Should().Be(1);
    }

    [Fact]
    public void EffectiveTarget_一般任務_回TargetValue()
    {
        var quest = new Quest { QuestType = QuestType.Count, TargetValue = 8 };
        Progression.IsProgression(quest).Should().BeFalse();
        Progression.EffectiveTarget(quest, 5).Should().Be(8);
    }

    [Fact]
    public void RenderName_時間類_以當階時間取代佔位()
    {
        Progression.RenderName(Bedtime(780, 720, 10, -5), 3).Should().Be("00:50 前上床睡覺");
    }

    [Fact]
    public void RenderName_一般任務_原名()
    {
        Progression.RenderName(new Quest { Name = "喝水" }, 0).Should().Be("喝水");
    }

    [Fact]
    public void RenderName_傍晚基準_以當階時間取代佔位()
    {
        // 起床以 18:00 為基準；08:00→07:00、10 階、每階 -5：第 1 階 840-5=835 → Format(835, 18) = 07:55
        Progression.RenderName(WakeTime(840, 780, 10, -5), 0).Should().Be("07:55 前起床");
    }

    [Fact]
    public void RenderFinalName_時間類_以終點取代佔位()
    {
        Progression.RenderFinalName(Bedtime(780, 720, 10, -5)).Should().Be("00:00 前上床睡覺");
    }

    [Fact]
    public void RenderFinalName_傍晚基準_以終點取代佔位()
    {
        Progression.RenderFinalName(WakeTime(840, 780, 10, -5)).Should().Be("07:00 前起床");
    }

    [Fact]
    public void RenderFinalName_一般任務_原名()
    {
        Progression.RenderFinalName(new Quest { Name = "喝水" }).Should().Be("喝水");
    }

    [Fact]
    public void TargetLabel_時間類與數字類()
    {
        Progression.TargetLabel(Bedtime(780, 720, 10, -5), 775).Should().Be("00:55");
        Progression.TargetLabel(Reading(10, 30, 10, 2), 12).Should().Be("12 分鐘");
        Progression.TargetLabel(Reading(3, 2, 4, -0.25m), 2.75m).Should().Be("2.75 分鐘");
    }

    [Fact]
    public void TargetLabel_傍晚基準時間類()
    {
        Progression.TargetLabel(WakeTime(840, 780, 10, -5), 835).Should().Be("07:55");
    }
}
