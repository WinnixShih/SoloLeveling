using FluentAssertions;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Goals;

namespace SoloLeveling.Domain.Tests;

public class GoalPlannerTests
{
    private static Dictionary<string, string> Routine(string bed, string targetBed, string wake, string targetWake, string days = "30")
    {
        return new Dictionary<string, string>
        {
            ["currentBedtime"] = bed,
            ["targetBedtime"] = targetBed,
            ["currentWakeTime"] = wake,
            ["targetWakeTime"] = targetWake,
            ["lengthDays"] = days,
        };
    }

    [Fact]
    public void Plan_作息_產生就寢與起床兩個Check任務()
    {
        var quests = GoalPlanner.Plan(GoalCategory.Routine, Routine("01:00", "00:00", "08:00", "07:00"));

        quests.Should().HaveCount(2);
        var bed = quests[0];
        bed.Name.Should().Be("{target} 前上床睡覺");
        bed.QuestType.Should().Be(QuestType.Check);
        bed.StatType.Should().Be(StatType.Vitality);
        bed.Difficulty.Should().Be(Difficulty.Normal);
        bed.ValueKind.Should().Be(ProgressionValueKind.TimeOfDay);
        bed.StartValue.Should().Be(780);
        bed.EndValue.Should().Be(720);
        bed.StageCount.Should().Be(10);
        bed.StepValue.Should().Be(-5);
        bed.DaysPerStep.Should().Be(3);
        var wake = quests[1];
        wake.Name.Should().Be("{target} 前起床");
        wake.Difficulty.Should().Be(Difficulty.Hard);
        wake.ValueKind.Should().Be(ProgressionValueKind.TimeOfDayEvening);
        wake.StartValue.Should().Be(840);
        wake.EndValue.Should().Be(780);
    }

    [Fact]
    public void Plan_作息_跨午夜提早_合法且逐階變小()
    {
        var quests = GoalPlanner.Plan(GoalCategory.Routine, Routine("00:30", "23:30", "08:00", "08:00"));

        quests[0].StartValue.Should().Be(750);
        quests[0].EndValue.Should().Be(690);
        quests[0].StepValue.Should().BeNegative();
        quests[1].StartValue.Should().Be(quests[1].EndValue);
        quests[1].StageCount.Should().Be(1);
        quests[1].StepValue.Should().Be(0);
    }

    [Fact]
    public void Plan_作息_起終點相同_只有一階且StepValue為0()
    {
        var quests = GoalPlanner.Plan(GoalCategory.Routine, Routine("01:00", "00:00", "08:00", "08:00"));

        var wake = quests[1];
        wake.StageCount.Should().Be(1);
        wake.StepValue.Should().Be(0);
    }

    [Fact]
    public void Plan_作息_中午後起床_合法且逐階提早()
    {
        var quests = GoalPlanner.Plan(GoalCategory.Routine, Routine("01:00", "00:00", "12:30", "07:00"));

        var wake = quests[1];
        wake.StartValue.Should().Be(1110);
        wake.EndValue.Should().Be(780);
        wake.StepValue.Should().BeNegative();
    }

    [Fact]
    public void Plan_作息_目標比現況晚_丟例外()
    {
        var act = () => GoalPlanner.Plan(GoalCategory.Routine, Routine("00:00", "01:00", "08:00", "07:00"));
        act.Should().Throw<DomainValidationException>().Which.Code.Should().Be("TargetNotBetter");
    }

    [Fact]
    public void Plan_閱讀_產生Count任務且目標遞增()
    {
        var answers = new Dictionary<string, string> { ["currentMinutes"] = "10", ["targetMinutes"] = "30", ["lengthDays"] = "30" };

        var quests = GoalPlanner.Plan(GoalCategory.Reading, answers);

        quests.Should().ContainSingle();
        var q = quests[0];
        q.Name.Should().Be("閱讀");
        q.QuestType.Should().Be(QuestType.Count);
        q.StatType.Should().Be(StatType.Intelligence);
        q.ValueKind.Should().Be(ProgressionValueKind.Number);
        q.Unit.Should().Be("分鐘");
        q.UiStep.Should().Be(5);
        q.StartValue.Should().Be(10);
        q.EndValue.Should().Be(30);
        q.StageCount.Should().Be(10);
        q.StepValue.Should().Be(2);
    }

    [Fact]
    public void Plan_運動_目標不得小於現況()
    {
        var answers = new Dictionary<string, string> { ["currentMinutes"] = "30", ["targetMinutes"] = "20", ["lengthDays"] = "30" };
        var act = () => GoalPlanner.Plan(GoalCategory.Exercise, answers);
        act.Should().Throw<DomainValidationException>().Which.Code.Should().Be("TargetNotBetter");
    }

    [Fact]
    public void Plan_螢幕時間_產生Limit任務且上限遞減()
    {
        var answers = new Dictionary<string, string> { ["currentHours"] = "4", ["targetHours"] = "2", ["lengthDays"] = "21" };

        var q = GoalPlanner.Plan(GoalCategory.ScreenTime, answers).Single();

        q.QuestType.Should().Be(QuestType.Limit);
        q.StatType.Should().Be(StatType.Willpower);
        q.Difficulty.Should().Be(Difficulty.Hard);
        q.Unit.Should().Be("小時");
        q.UiStep.Should().Be(0.5m);
        q.StageCount.Should().Be(7);
        q.StepValue.Should().Be(-0.25m);
    }

    [Fact]
    public void Plan_螢幕時間_差距小天數長_減少階數拉長每階天數()
    {
        var answers = new Dictionary<string, string> { ["currentHours"] = "4", ["targetHours"] = "3", ["lengthDays"] = "50" };

        var q = GoalPlanner.Plan(GoalCategory.ScreenTime, answers).Single();

        q.StageCount.Should().Be(4);
        q.DaysPerStep.Should().Be(13);
        q.StepValue.Should().Be(-0.25m);
    }

    [Fact]
    public void Plan_作息_就寢差距10分鐘_兩階各15天()
    {
        var quests = GoalPlanner.Plan(GoalCategory.Routine, Routine("01:00", "00:50", "08:00", "08:00"));

        var bed = quests[0];
        bed.StageCount.Should().Be(2);
        bed.DaysPerStep.Should().Be(15);
        bed.StepValue.Should().Be(-5);
    }

    [Theory]
    [InlineData("6")]
    [InlineData("91")]
    [InlineData("abc")]
    public void Plan_天數超出範圍或非數字_丟例外(string days)
    {
        var act = () => GoalPlanner.Plan(GoalCategory.Routine, Routine("01:00", "00:00", "08:00", "07:00", days));
        act.Should().Throw<DomainValidationException>().Which.Code.Should().Be("InvalidLengthDays");
    }

    [Fact]
    public void Plan_缺少回答鍵_丟DomainValidationException()
    {
        var answers = new Dictionary<string, string> { ["currentMinutes"] = "10", ["lengthDays"] = "30" };
        var act = () => GoalPlanner.Plan(GoalCategory.Reading, answers);
        act.Should().Throw<DomainValidationException>().Which.Code.Should().Be("MissingAnswer");
    }

    [Theory]
    [InlineData("1,0")]
    [InlineData("1 000")]
    [InlineData("+")]
    public void Plan_數值含千分位或非法符號_丟InvalidAnswer(string value)
    {
        var answers = new Dictionary<string, string> { ["currentMinutes"] = "10", ["targetMinutes"] = value, ["lengthDays"] = "30" };
        var act = () => GoalPlanner.Plan(GoalCategory.Reading, answers);
        act.Should().Throw<DomainValidationException>().Which.Code.Should().Be("InvalidAnswer");
    }

    [Fact]
    public void Plan_帶類別沒有的回答鍵_丟UnknownAnswer()
    {
        var answers = new Dictionary<string, string> { ["currentMinutes"] = "10", ["targetMinutes"] = "30", ["lengthDays"] = "30", ["bogus"] = "1" };
        var act = () => GoalPlanner.Plan(GoalCategory.Reading, answers);
        act.Should().Throw<DomainValidationException>().Which.Code.Should().Be("UnknownAnswer");
    }

    [Fact]
    public void Plan_數值超出問題範圍_丟例外()
    {
        var answers = new Dictionary<string, string> { ["currentMinutes"] = "10", ["targetMinutes"] = "999", ["lengthDays"] = "30" };
        var act = () => GoalPlanner.Plan(GoalCategory.Reading, answers);
        act.Should().Throw<DomainValidationException>().Which.Code.Should().Be("InvalidAnswer");
    }

    [Fact]
    public void GoalCategories_四個類別且取代索引正確()
    {
        GoalCategories.All.Select(c => c.Category).Should().Equal(GoalCategory.Routine, GoalCategory.Exercise, GoalCategory.Reading, GoalCategory.ScreenTime);
        GoalCategories.Get(GoalCategory.Routine).ReplacesBasicQuestIndexes.Should().Equal(0, 1);
        GoalCategories.Get(GoalCategory.Exercise).ReplacesBasicQuestIndexes.Should().Equal(7);
        GoalCategories.Get(GoalCategory.Reading).ReplacesBasicQuestIndexes.Should().Equal(3);
        GoalCategories.Get(GoalCategory.ScreenTime).ReplacesBasicQuestIndexes.Should().Equal(5);
        GoalCategories.All.Should().OnlyContain(c => c.Questions.Any(q => q.Key == "lengthDays"));
    }
}
