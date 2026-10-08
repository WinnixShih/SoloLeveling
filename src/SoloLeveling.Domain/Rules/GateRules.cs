using SoloLeveling.Domain.Entities;

namespace SoloLeveling.Domain.Rules;

/// <summary>
/// 傳送門的純規則：第幾天、最後一天、通關與失敗判定。已漏天數由呼叫端從已結算且未達標的 DailyLog 算好傳入。
/// </summary>
public static class GateRules
{
    /// <summary>
    /// 門的最後一天。
    /// </summary>
    /// <param name="gate">門。</param>
    /// <returns>開門日 + 天數 − 1。</returns>
    public static DateOnly LastDay(Gate gate)
    {
        return gate.StartDate.AddDays(gate.LengthDays - 1);
    }

    /// <summary>
    /// 顯示用的第幾天：開門日為 1，夾在 1 到天數之間（改時區可能讓今日早於開門日）。
    /// </summary>
    /// <param name="gate">門。</param>
    /// <param name="today">使用者時區的今日。</param>
    /// <returns>1 到 <see cref="Gate.LengthDays"/>。</returns>
    public static int DayNumber(Gate gate, DateOnly today)
    {
        return Math.Clamp(today.DayNumber - gate.StartDate.DayNumber + 1, 1, gate.LengthDays);
    }

    /// <summary>
    /// 判定進行中的門：已漏天數超過上限即失敗；今天是最後一天且已達標、或今天已過最後一天，即通關；否則進行中。
    /// </summary>
    /// <param name="gate">進行中的門。</param>
    /// <param name="today">使用者時區的今日。</param>
    /// <param name="misses">門期間內已結算且未達標的天數（保險卡保住的日子也算）。</param>
    /// <param name="todayCleared">今日目前是否達標。</param>
    /// <returns>判定結果。</returns>
    public static GateVerdict Judge(Gate gate, DateOnly today, int misses, bool todayCleared)
    {
        if (misses > gate.AllowedMisses)
        {
            return GateVerdict.Failed;
        }

        var lastDay = LastDay(gate);
        if (today > lastDay || (today == lastDay && todayCleared))
        {
            return GateVerdict.Cleared;
        }

        return GateVerdict.InProgress;
    }
}
