using Microsoft.EntityFrameworkCore;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Services;

/// <summary>
/// 成就判定所需的統計與已解鎖成就。
/// </summary>
/// <param name="Stats">統計快照。</param>
/// <param name="UnlockedKeys">已解鎖的成就鍵。</param>
public sealed record RewardState(AchievementStats Stats, IReadOnlySet<string> UnlockedKeys);

/// <summary>
/// 批次查出成就統計（固定 7 次查詢，不隨任務數增加）。必須在本次請求的修改 SaveChanges 之後呼叫，查詢才看得到今天的進度與新建的目標。
/// </summary>
/// <param name="db">DbContext。</param>
public class RewardStatsLoader(AppDbContext db)
{
    /// <summary>
    /// 載入統計。
    /// </summary>
    /// <param name="player">玩家（已反映本次修改；BestStreak、TotalCompleted、等級直接取自此實體）。</param>
    /// <param name="today">使用者時區的今日。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>統計與已解鎖成就。</returns>
    public async Task<RewardState> LoadAsync(Player player, DateOnly today, CancellationToken ct)
    {
        var userId = player.UserId;
        var goalCategories = await db.Goals
            .Where(g => g.UserId == userId)
            .Select(g => new { g.Id, g.Category })
            .ToDictionaryAsync(g => g.Id, g => g.Category, ct);
        var goalQuests = await db.Quests
            .Where(q => q.UserId == userId && q.GoalId != null)
            .Select(q => new { q.Id, GoalId = q.GoalId!.Value, q.ValueKind, q.IsArchived })
            .ToListAsync(ct);
        var roles = goalQuests
            .Select(q => new { q.Id, q.IsArchived, Role = QuestRoles.Of(goalCategories[q.GoalId], q.ValueKind) })
            .Where(q => q.Role != null)
            .ToList();

        // 分類連續：只看未封存的角色任務，封存重建目標後以新任務重新起算
        var activeRoles = roles.Where(r => !r.IsArchived).ToList();
        var activeRoleIds = activeRoles.Select(r => r.Id).ToList();
        var since = today.AddDays(-QuestRoles.StreakWindowDays);
        var doneRows = activeRoleIds.Count == 0
            ? new List<DoneRow>()
            : await db.QuestProgresses
                .Join(db.DailyLogs, p => p.DailyLogId, l => l.Id, (p, l) => new { p.QuestId, p.IsDone, l.Date })
                .Where(x => x.IsDone && x.Date >= since && x.Date <= today && activeRoleIds.Contains(x.QuestId))
                .Select(x => new DoneRow(x.QuestId, x.Date))
                .ToListAsync(ct);
        var datesByQuest = doneRows.ToLookup(r => r.QuestId, r => r.Date);
        var roleStreaks = activeRoles
            .GroupBy(r => r.Role!.Value)
            .ToDictionary(g => g.Key, g => g.Max(r => QuestRoles.StreakOf(datesByQuest[r.Id].ToHashSet(), today)));

        // 分類累計分鐘：含已封存任務（終身累計）
        var minuteRoles = roles.Where(r => QuestRoles.CountsMinutes(r.Role!.Value)).ToList();
        var minuteIds = minuteRoles.Select(r => r.Id).ToList();
        var minutesByQuest = minuteIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await db.QuestProgresses
                .Where(p => minuteIds.Contains(p.QuestId) && p.Value != null)
                .GroupBy(p => p.QuestId)
                .Select(g => new { QuestId = g.Key, Total = g.Sum(p => p.Value!.Value) })
                .ToDictionaryAsync(x => x.QuestId, x => x.Total, ct);
        var roleMinutes = minuteRoles
            .GroupBy(r => r.Role!.Value)
            .ToDictionary(g => g.Key, g => (int)Math.Floor(g.Sum(r => minutesByQuest.GetValueOrDefault(r.Id))));

        var ownedCardKinds = await db.OwnedCards.CountAsync(c => c.UserId == userId, ct);
        var completedPrograms = await db.Programs.CountAsync(p => p.UserId == userId && p.CompletedAt != null, ct);
        var unlocked = await db.Achievements.Where(a => a.UserId == userId).Select(a => a.Key).ToListAsync(ct);

        var stats = new AchievementStats(
            goalCategories.Count,
            player.BestStreak,
            player.TotalCompleted,
            roleStreaks,
            roleMinutes,
            ownedCardKinds,
            completedPrograms,
            Math.Max(player.PeakLevel, player.Level));
        return new RewardState(stats, unlocked.ToHashSet());
    }

    private sealed record DoneRow(Guid QuestId, DateOnly Date);
}
