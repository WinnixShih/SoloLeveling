using Microsoft.EntityFrameworkCore;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Services;

/// <summary>
/// 每個會改玩家狀態的請求在第一次 <c>SaveChangesAsync</c> 之後呼叫一次：判定目標完成、批次查統計、執行
/// <see cref="Rewards.Evaluate"/>，把寶箱、成就、金幣事件寫入並回報未公告的保險卡事件。仍在呼叫端的交易內，不 commit。
/// </summary>
/// <param name="db">DbContext。</param>
/// <param name="statsLoader">成就統計載入。</param>
/// <param name="clock">時間來源。</param>
public class RewardApplier(AppDbContext db, RewardStatsLoader statsLoader, TimeProvider clock)
{
    /// <summary>
    /// 判定並持久化本次請求的獎勵；方法結尾會再 SaveChanges 一次。
    /// </summary>
    /// <param name="context">今日內容（已反映本次修改並存檔）。</param>
    /// <param name="completedPrograms">本次請求新完成的 66 天週期數（只有開新週期會傳 1）。</param>
    /// <param name="ct">取消權杖。</param>
    /// <param name="alreadyCompletedGoals">呼叫端先以 <see cref="CompleteFinishedGoalsAsync"/> 標記完成的目標數（封存會讓目標不再被判定，要先標記）。</param>
    /// <returns>本次獎勵，放進回應的 <c>rewards</c> 欄位。</returns>
    public async Task<RewardsDto> ApplyAsync(TodayContext context, int completedPrograms, CancellationToken ct, int alreadyCompletedGoals = 0)
    {
        var now = clock.GetUtcNow();
        var player = context.Player;
        var completedGoals = alreadyCompletedGoals + await CompleteFinishedGoalsAsync(context, ct);
        player.RewardsEvaluatedDate = context.Today;
        var state = await statsLoader.LoadAsync(player, context.Today, ct);
        var outcome = Rewards.Evaluate(new RewardInput(
            context.Before,
            player,
            context.TodayLog,
            state.UnlockedKeys,
            state.Stats,
            completedGoals,
            completedPrograms));

        player.PeakLevel = outcome.NewPeakLevel;
        player.ShieldCount += outcome.ShieldsGained;
        context.TodayLog.ClearCoinsGranted = outcome.ClearCoinsGranted;

        var chests = outcome.Chests
            .Select(c => new RewardChest { Id = Guid.NewGuid(), UserId = player.UserId, Rarity = c.Rarity, Source = c.Source, CreatedAt = now })
            .ToList();
        db.RewardChests.AddRange(chests);
        db.Achievements.AddRange(outcome.NewAchievements.Select(a => new Achievement { UserId = player.UserId, Key = a.Key, UnlockedAt = now }));
        foreach (var grant in outcome.Coins)
        {
            Guid? refId = grant.Source is CoinSource.DailyClear or CoinSource.DailyClearUndo ? context.TodayLog.Id : null;
            db.CoinEvents.Add(Wallet.Change(player, grant.Amount, grant.Source, refId, now));
        }

        var shieldEvents = await db.RewardEvents
            .Where(e => e.UserId == player.UserId && e.Kind == RewardEventKind.ShieldUsed && e.AnnouncedAt == null)
            .OrderBy(e => e.Date)
            .ToListAsync(ct);
        foreach (var shieldEvent in shieldEvents)
        {
            shieldEvent.AnnouncedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return new RewardsDto(
            outcome.LevelsGained,
            outcome.RankUps.ToList(),
            chests.Select(c => c.ToDto()).ToList(),
            outcome.NewAchievements.Select(a => a.ToUnlockedDto()).ToList(),
            outcome.Coins.Sum(c => c.Amount),
            outcome.ShieldsGained,
            shieldEvents.Select(e => e.Date).ToList(),
            player.ShieldCount);
    }

    /// <summary>
    /// 讀取端點（GET /today、GET /me）用：本次請求新結算了日子、有待公告的保險卡事件、或今天還沒完整判定過時，等同 <see cref="ApplyAsync"/>；
    /// 其餘情況略過統計查詢與獎勵判定，只回傳空獎勵。因此每位使用者每天至少完整判定一次，排程在請求之外結算造成的獎勵最晚在當天第一次讀取補發。
    /// </summary>
    /// <param name="context">今日內容。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>本次獎勵，放進回應的 <c>rewards</c> 欄位。</returns>
    public async Task<RewardsDto> ApplyOnReadAsync(TodayContext context, CancellationToken ct)
    {
        if (context.SettledDays > 0
            || context.Player.RewardsEvaluatedDate != context.Today
            || await db.RewardEvents.AnyAsync(e => e.UserId == context.User.Id && e.Kind == RewardEventKind.ShieldUsed && e.AnnouncedAt == null, ct))
        {
            return await ApplyAsync(context, 0, ct);
        }

        return new RewardsDto(0, [], [], [], 0, 0, [], context.Player.ShieldCount);
    }

    /// <summary>
    /// 把本次達成完成條件的目標寫入 <see cref="Goal.CompletedAt"/>；已完成的不再判定，所以 A 級寶箱只發一次。
    /// 要封存目標或任務的端點，須在封存前呼叫，否則走完期間當天封存會讓目標永遠不被判定完成。
    /// </summary>
    /// <param name="context">今日內容。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>本次新完成的目標數。</returns>
    public async Task<int> CompleteFinishedGoalsAsync(TodayContext context, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var goals = await db.Goals
            .Where(g => g.UserId == context.User.Id && !g.IsArchived && g.CompletedAt == null)
            .ToListAsync(ct);
        var doneToday = context.TodayLog.Progresses.Where(p => p.IsDone).Select(p => p.QuestId).ToHashSet();
        var completed = 0;
        foreach (var goal in goals)
        {
            var quests = context.ActiveQuests.Where(q => q.GoalId == goal.Id).ToList();
            if (GoalCompletion.IsFinished(goal, context.Today, quests, q => context.DoneDaysOf(q.Id) + (doneToday.Contains(q.Id) ? 1 : 0)))
            {
                goal.CompletedAt = now;
                completed += 1;
            }
        }

        return completed;
    }
}
