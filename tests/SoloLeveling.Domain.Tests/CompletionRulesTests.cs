using FluentAssertions;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class CompletionRulesTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(0.0, false)]
    [InlineData(1.0, true)]
    public void IsDone_Check類型_value大於等於1即完成(double? value, bool expected)
    {
        CompletionRules.IsDone(QuestType.Check, (decimal?)value, null).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(7.0, false)]
    [InlineData(8.0, true)]
    [InlineData(9.0, true)]
    public void IsDone_Count類型_達到目標即完成(double? value, bool expected)
    {
        CompletionRules.IsDone(QuestType.Count, (decimal?)value, 8m).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(2.5, true)]
    [InlineData(3.0, true)]
    [InlineData(3.5, false)]
    public void IsDone_Limit類型_未填不算完成_不超過上限才完成(double? value, bool expected)
    {
        CompletionRules.IsDone(QuestType.Limit, (decimal?)value, 3m).Should().Be(expected);
    }

    [Theory]
    [InlineData(Difficulty.Easy, 10, 1)]
    [InlineData(Difficulty.Normal, 20, 1)]
    [InlineData(Difficulty.Hard, 35, 2)]
    public void RewardOf_依難度回傳EXP與屬性獎勵(Difficulty difficulty, int xp, int stat)
    {
        var reward = CompletionRules.RewardOf(difficulty);

        reward.Xp.Should().Be(xp);
        reward.Stat.Should().Be(stat);
    }

    [Theory]
    [InlineData(false, 0.70)]
    [InlineData(true, 1.00)]
    public void ThresholdOf_一般70_困難100(bool hardMode, double expected)
    {
        CompletionRules.ThresholdOf(hardMode).Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(7, 10, 0.7)]
    [InlineData(2, 3, 0.6667)]
    public void CompletionRatio_無任務為0_其餘四捨五入到小數4位(int done, int active, double expected)
    {
        CompletionRules.CompletionRatio(done, active).Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData(1, 15)]   // 100 * 0.15
    [InlineData(2, 18)]   // 120 * 0.15
    [InlineData(4, 24)]   // 160 * 0.15
    [InlineData(6, 30)]   // 200 * 0.15
    public void PenaltyOf_為XpNeeded的15percent四捨五入(int level, int expected)
    {
        CompletionRules.PenaltyOf(level).Should().Be(expected);
    }
}
