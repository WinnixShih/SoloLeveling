using Microsoft.EntityFrameworkCore;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Errors;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Services;

/// <summary>
/// 任務的查詢、新增、修改、封存與排序。新增／修改／封存都會改變今日達標率的分母或已發獎勵，因此一律在結算後的今日內容上操作並重算。
/// </summary>
/// <param name="db">DbContext。</param>
/// <param name="loader">今日內容載入（含結算）。</param>
/// <param name="clock">時間來源。</param>
public class QuestService(AppDbContext db, TodayContextLoader loader, TimeProvider clock)
{
    /// <summary>
    /// GET /quests：未封存任務，依 SortOrder 排序。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>任務清單。</returns>
    public async Task<List<QuestDto>> ListAsync(Guid userId, CancellationToken ct)
    {
        return await db.Quests.AsNoTracking()
            .Where(q => q.UserId == userId && !q.IsArchived)
            .OrderBy(q => q.SortOrder)
            .Select(q => q.ToDto())
            .ToListAsync(ct);
    }

    /// <summary>
    /// POST /quests：新增任務並排在最後。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="request">任務內容。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>新任務。</returns>
    public async Task<QuestDto> CreateAsync(Guid userId, QuestRequest request, CancellationToken ct)
    {
        Validate(request);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);

        var quest = new Quest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SortOrder = context.ActiveQuests.Count == 0 ? 0 : context.ActiveQuests.Max(q => q.SortOrder) + 1,
            CreatedAt = clock.GetUtcNow(),
        };
        Apply(quest, request);
        db.Quests.Add(quest);
        context.ActiveQuests.Add(quest);

        db.XpEvents.AddRange(ProgressUpdater.Recalculate(context.Player, context.TodayLog, context.ActiveQuests, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return quest.ToDto();
    }

    /// <summary>
    /// PUT /quests/{id}：修改任務；改任務類型時清除該任務今日進度並撤銷已發獎勵（規格 4.9）。
    /// 漸進任務（<see cref="Progression.IsProgression"/>）的目標由公式決定，只開放修改屬性與難度，其餘欄位不同一律回 400。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="questId">任務 ID。</param>
    /// <param name="request">任務內容。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>更新後的任務。</returns>
    /// <exception cref="ApiErrorException">任務不存在或不屬於此使用者（404）；漸進任務嘗試修改名稱、任務類型、目標值、增減量或單位（400，錯誤碼 <c>ProgressionQuestLocked</c>）。</exception>
    public async Task<QuestDto> UpdateAsync(Guid userId, Guid questId, QuestRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        var quest = FindActive(context, questId);
        var now = clock.GetUtcNow();

        if (Progression.IsProgression(quest))
        {
            var unchanged = request.Name.Trim() == quest.Name
                && request.QuestType == quest.QuestType
                && request.TargetValue == quest.TargetValue
                && request.Step == quest.Step
                && request.Unit?.Trim() == quest.Unit;
            if (!unchanged)
            {
                throw ApiErrorException.BadRequest("ProgressionQuestLocked", "漸進任務只能修改屬性與難度；要改目標請封存目標後重新建立");
            }

            quest.StatType = request.StatType;
            quest.Difficulty = request.Difficulty;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return quest.ToDto();
        }

        Validate(request);
        if (quest.QuestType != request.QuestType)
        {
            db.XpEvents.AddRange(ProgressUpdater.ClearQuestProgress(context.Player, context.TodayLog, context.ActiveQuests, quest, now));
            db.QuestProgresses.RemoveRange(db.QuestProgresses.Local.Where(p => p.QuestId == quest.Id && p.DailyLogId == context.TodayLog.Id));
        }

        Apply(quest, request);
        db.XpEvents.AddRange(ProgressUpdater.Recalculate(context.Player, context.TodayLog, context.ActiveQuests, now));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return quest.ToDto();
    }

    /// <summary>
    /// DELETE /quests/{id}：封存任務並重算今日達標。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="questId">任務 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>非同步作業。</returns>
    /// <exception cref="ApiErrorException">任務不存在或不屬於此使用者（404）。</exception>
    public async Task ArchiveAsync(Guid userId, Guid questId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        var quest = FindActive(context, questId);
        var now = clock.GetUtcNow();

        quest.IsArchived = true;
        quest.ArchivedAt = now;
        context.ActiveQuests.Remove(quest);

        db.XpEvents.AddRange(ProgressUpdater.Recalculate(context.Player, context.TodayLog, context.ActiveQuests, now));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// PUT /quests/reorder：依給定順序重設 SortOrder；ID 集合必須恰好等於全部未封存任務。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="questIds">新順序。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>非同步作業。</returns>
    /// <exception cref="ApiErrorException">ID 集合與未封存任務不一致（400）。</exception>
    public async Task ReorderAsync(Guid userId, List<Guid> questIds, CancellationToken ct)
    {
        var quests = await db.Quests.Where(q => q.UserId == userId && !q.IsArchived).ToListAsync(ct);
        if (questIds.Distinct().Count() != quests.Count || !quests.All(q => questIds.Contains(q.Id)))
        {
            throw ApiErrorException.BadRequest("InvalidQuestIds", "questIds 必須恰好包含全部未封存任務");
        }

        var byId = quests.ToDictionary(q => q.Id);
        for (var i = 0; i < questIds.Count; i++)
        {
            byId[questIds[i]].SortOrder = i;
        }

        await db.SaveChangesAsync(ct);
    }

    private static Quest FindActive(TodayContext context, Guid questId)
    {
        return context.ActiveQuests.SingleOrDefault(q => q.Id == questId)
            ?? throw ApiErrorException.NotFound("QuestNotFound", "任務不存在");
    }

    private static void Validate(QuestRequest request)
    {
        var name = request.Name.Trim();
        if (name.Length is 0 or > 60)
        {
            throw ApiErrorException.BadRequest("InvalidName", "任務名稱需為 1–60 字");
        }

        if (request.QuestType != QuestType.Check && request.TargetValue is not > 0)
        {
            throw ApiErrorException.BadRequest("TargetValueRequired", "Count／Limit 類型必須提供大於 0 的 targetValue");
        }

        if (request.Step is < 0)
        {
            throw ApiErrorException.BadRequest("InvalidStep", "step 不可為負");
        }

        if (request.Unit is { Length: > 10 })
        {
            throw ApiErrorException.BadRequest("InvalidUnit", "unit 最長 10 字");
        }
    }

    private static void Apply(Quest quest, QuestRequest request)
    {
        quest.Name = request.Name.Trim();
        quest.StatType = request.StatType;
        quest.Difficulty = request.Difficulty;
        quest.QuestType = request.QuestType;
        var isCheck = request.QuestType == QuestType.Check;
        quest.TargetValue = isCheck ? null : request.TargetValue;
        quest.Step = isCheck ? null : request.Step;
        quest.Unit = isCheck ? null : request.Unit?.Trim();
    }
}
