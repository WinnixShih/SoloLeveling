using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Errors;
using SoloLeveling.Domain.Rules;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Services;

/// <summary>
/// 今日任務、進度寫入與反思筆記。
/// </summary>
/// <param name="db">DbContext。</param>
/// <param name="loader">今日內容載入（含結算）。</param>
/// <param name="clock">時間來源。</param>
public class TodayService(AppDbContext db, TodayContextLoader loader, TimeProvider clock)
{
    /// <summary>反思筆記最長字數。</summary>
    public const int MaxNoteLength = 2000;

    /// <summary>
    /// GET /today。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>今日任務與進度。</returns>
    public async Task<TodayResponse> GetTodayAsync(Guid userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        await tx.CommitAsync(ct);
        return Build(context);
    }

    /// <summary>
    /// PUT /today/quests/{id}/progress：寫入進度並套用 EXP／屬性／達標獎勵（規格 4.4）。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="questId">任務 ID。</param>
    /// <param name="value">新值。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>更新後的今日。</returns>
    /// <exception cref="ApiErrorException">任務不存在或已封存（404）。</exception>
    public async Task<TodayResponse> SetProgressAsync(Guid userId, Guid questId, decimal? value, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        var quest = context.ActiveQuests.SingleOrDefault(q => q.Id == questId)
            ?? throw ApiErrorException.NotFound("QuestNotFound", "任務不存在");

        var existingProgressIds = context.TodayLog.Progresses.Select(p => p.Id).ToHashSet();
        var events = ProgressUpdater.SetValue(context.Player, context.TodayLog, context.ActiveQuests, quest, value, clock.GetUtcNow());
        // 透過導覽集合新增、且主鍵已設值的實體會被 EF 當成既有資料（Modified），必須明確標成 Added
        db.QuestProgresses.AddRange(context.TodayLog.Progresses.Where(p => !existingProgressIds.Contains(p.Id)));
        db.XpEvents.AddRange(events);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Build(context);
    }

    /// <summary>
    /// PUT /today/note。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="note">內容。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>非同步作業。</returns>
    /// <exception cref="ApiErrorException">超過 2000 字（400）。</exception>
    public async Task SetNoteAsync(Guid userId, string note, CancellationToken ct)
    {
        if (note.Length > MaxNoteLength)
        {
            throw ApiErrorException.BadRequest("NoteTooLong", $"反思最長 {MaxNoteLength} 字");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        context.TodayLog.Note = note;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static TodayResponse Build(TodayContext context)
    {
        var progressByQuest = context.TodayLog.Progresses.ToDictionary(p => p.QuestId);
        var quests = context.ActiveQuests.Select(q =>
        {
            progressByQuest.TryGetValue(q.Id, out var progress);
            var reward = CompletionRules.RewardOf(q.Difficulty);
            return new TodayQuestDto(
                q.Id, q.Name, q.StatType, q.Difficulty, q.QuestType, q.TargetValue, q.Step, q.Unit, q.SortOrder,
                progress?.Value, progress?.IsDone ?? false, reward.Xp, reward.Stat);
        }).ToList();

        var log = context.TodayLog;
        return new TodayResponse(
            context.Today,
            log.CompletionRatio,
            log.IsCleared,
            CompletionRules.ThresholdOf(context.Player.HardMode),
            log.BonusGranted,
            log.Note,
            quests);
    }
}
