using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Errors;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Goals;
using SoloLeveling.Domain.Rules;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Services;

/// <summary>
/// 引導式目標：類別定義、預覽、建立（連同基本任務）、列表與封存。建立與封存會改變今日達標率分母，因此在結算後的今日內容上操作並重算。
/// </summary>
/// <param name="db">DbContext。</param>
/// <param name="loader">今日內容載入（含結算）。</param>
/// <param name="rewardApplier">獎勵判定與持久化。</param>
/// <param name="clock">時間來源。</param>
public class GoalService(AppDbContext db, TodayContextLoader loader, RewardApplier rewardApplier, TimeProvider clock)
{
    /// <summary>
    /// GET /goals/categories：類別定義與基本任務清單。
    /// </summary>
    /// <returns>定義。</returns>
    public static CategoriesResponse Categories()
    {
        var categories = GoalCategories.All.Select(c => new GoalCategoryDto(
            c.Category,
            c.Title,
            c.Questions.Select(q => new GoalQuestionDto(q.Key, q.Label, q.Type, q.Min, q.Max, q.Default)).ToList(),
            c.ReplacesBasicQuestIndexes.ToList())).ToList();
        var basics = DefaultQuests.All.Select((t, i) => new BasicQuestDto(i, t.Name, t.StatType, t.Difficulty)).ToList();
        return new CategoriesResponse(categories, basics);
    }

    /// <summary>
    /// POST /goals/preview：只計算不寫入。
    /// </summary>
    /// <param name="request">輸入。</param>
    /// <returns>每個目標會產生的任務與階段摘要。</returns>
    /// <exception cref="ApiErrorException">類別重複或基本任務索引不合法（400）。</exception>
    public static PreviewResponse Preview(CreateGoalsRequest request)
    {
        var plans = PlanAll(request);
        return new PreviewResponse(plans.Select(p => new GoalPreviewDto(
            p.Definition.Category,
            p.Definition.Title,
            p.Blueprints.Select(ToPreview).ToList())).ToList());
    }

    /// <summary>
    /// POST /goals：建立目標、其漸進任務與勾選的基本任務。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="request">輸入。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>建立後的進行中目標。</returns>
    /// <exception cref="ApiErrorException">輸入不合法（400）或同類別已有進行中的目標（409）。</exception>
    public async Task<GoalsResponse> CreateAsync(Guid userId, CreateGoalsRequest request, CancellationToken ct)
    {
        var plans = PlanAll(request);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        var now = clock.GetUtcNow();

        var activeCategories = await db.Goals.Where(g => g.UserId == userId && !g.IsArchived).Select(g => g.Category).ToListAsync(ct);
        var clash = plans.Select(p => p.Definition.Category).FirstOrDefault(activeCategories.Contains);
        if (clash != default)
        {
            throw ApiErrorException.Conflict("GoalAlreadyActive", $"「{GoalCategories.Get(clash).Title}」已有進行中的目標");
        }

        var sortOrder = context.ActiveQuests.Count == 0 ? 0 : context.ActiveQuests.Max(q => q.SortOrder) + 1;
        foreach (var plan in plans)
        {
            var goal = new Goal
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Category = plan.Definition.Category,
                Answers = JsonSerializer.Serialize(plan.Answers),
                LengthDays = plan.LengthDays,
                StartDate = context.Today,
                CreatedAt = now,
            };
            db.Goals.Add(goal);
            foreach (var b in plan.Blueprints)
            {
                var quest = new Quest
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Name = b.Name,
                    StatType = b.StatType,
                    Difficulty = b.Difficulty,
                    QuestType = b.QuestType,
                    Step = b.UiStep,
                    Unit = b.Unit,
                    SortOrder = sortOrder++,
                    CreatedAt = now,
                    GoalId = goal.Id,
                    ValueKind = b.ValueKind,
                    StartValue = b.StartValue,
                    EndValue = b.EndValue,
                    StepValue = b.StepValue,
                    StageCount = b.StageCount,
                    DaysPerStep = b.DaysPerStep,
                };
                db.Quests.Add(quest);
                context.ActiveQuests.Add(quest);
            }
        }

        foreach (var index in request.BasicQuestIndexes ?? [])
        {
            var t = DefaultQuests.All[index];
            var quest = new Quest
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = t.Name,
                StatType = t.StatType,
                Difficulty = t.Difficulty,
                QuestType = t.QuestType,
                TargetValue = t.TargetValue,
                Step = t.Step,
                Unit = t.Unit,
                SortOrder = sortOrder++,
                CreatedAt = now,
            };
            db.Quests.Add(quest);
            context.ActiveQuests.Add(quest);
        }

        db.XpEvents.AddRange(ProgressUpdater.Recalculate(context.Player, context.TodayLog, context.ActiveQuests, now));
        await db.SaveChangesAsync(ct);
        var rewards = await rewardApplier.ApplyAsync(context, 0, ct);
        var response = await BuildListAsync(userId, context, ct);
        await tx.CommitAsync(ct);
        return response with { Rewards = rewards };
    }

    /// <summary>
    /// GET /goals：進行中的目標與各任務的當前階段，依建立順序排序（見 <see cref="BuildListAsync"/>）。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>目標清單。</returns>
    public async Task<GoalsResponse> ListAsync(Guid userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        var response = await BuildListAsync(userId, context, ct);
        await tx.CommitAsync(ct);
        return response;
    }

    /// <summary>
    /// DELETE /goals/{id}：封存目標與其未封存任務並重算今日達標。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="goalId">目標 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>本次獎勵（封存可能讓今日達標）。</returns>
    /// <exception cref="ApiErrorException">目標不存在或已封存（404）。</exception>
    public async Task<RewardsDto> ArchiveAsync(Guid userId, Guid goalId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        var goal = await db.Goals.SingleOrDefaultAsync(g => g.Id == goalId && g.UserId == userId && !g.IsArchived, ct)
            ?? throw ApiErrorException.NotFound("GoalNotFound", "目標不存在");
        var now = clock.GetUtcNow();

        goal.IsArchived = true;
        goal.ArchivedAt = now;
        foreach (var quest in context.ActiveQuests.Where(q => q.GoalId == goalId).ToList())
        {
            quest.IsArchived = true;
            quest.ArchivedAt = now;
            context.ActiveQuests.Remove(quest);
        }

        db.XpEvents.AddRange(ProgressUpdater.Recalculate(context.Player, context.TodayLog, context.ActiveQuests, now));
        await db.SaveChangesAsync(ct);
        var rewards = await rewardApplier.ApplyAsync(context, 0, ct);
        await tx.CommitAsync(ct);
        return rewards;
    }

    private sealed record GoalPlan(GoalCategoryDefinition Definition, IReadOnlyDictionary<string, string> Answers, int LengthDays, IReadOnlyList<GoalQuestBlueprint> Blueprints);

    private static List<GoalPlan> PlanAll(CreateGoalsRequest request)
    {
        if (request.Goals.Count == 0 && (request.BasicQuestIndexes ?? []).Count == 0)
        {
            throw ApiErrorException.BadRequest("NoGoals", "至少要選一個目標或基本任務");
        }

        if (request.Goals.Select(g => g.Category).Distinct().Count() != request.Goals.Count)
        {
            throw ApiErrorException.BadRequest("DuplicateCategory", "同一類別只能出現一次");
        }

        var plans = request.Goals.Select(g =>
        {
            var answers = g.AnswersAsStrings();
            var definition = GoalCategories.Get(g.Category);
            return new GoalPlan(definition, answers, GoalPlanner.LengthDaysOf(answers), GoalPlanner.Plan(g.Category, answers));
        }).ToList();

        var indexes = request.BasicQuestIndexes ?? [];
        if (indexes.Any(i => i < 0 || i >= DefaultQuests.All.Count) || indexes.Distinct().Count() != indexes.Count)
        {
            throw ApiErrorException.BadRequest("InvalidBasicQuestIndex", "basicQuestIndexes 不合法");
        }

        var replaced = plans.SelectMany(p => p.Definition.ReplacesBasicQuestIndexes).ToHashSet();
        if (indexes.Any(replaced.Contains))
        {
            throw ApiErrorException.BadRequest("BasicQuestReplaced", "選了會被目標取代的基本任務");
        }

        return plans;
    }

    private static PreviewQuestDto ToPreview(GoalQuestBlueprint b)
    {
        var probe = new Quest
        {
            Name = b.Name,
            QuestType = b.QuestType,
            Unit = b.Unit,
            GoalId = Guid.Empty,
            ValueKind = b.ValueKind,
            StartValue = b.StartValue,
            EndValue = b.EndValue,
            StepValue = b.StepValue,
            StageCount = b.StageCount,
            DaysPerStep = b.DaysPerStep,
        };
        var magnitude = Math.Abs(b.StepValue);
        var stepLabel = b.StepValue == 0
            ? "維持"
            : Progression.IsTimeKind(b.ValueKind)
                ? $"提早 {magnitude:0.##} 分鐘"
                : $"{(b.StepValue > 0 ? "增加" : "減少")} {Progression.TargetLabel(probe, magnitude)}";
        return new PreviewQuestDto(
            Progression.RenderName(probe, 0),
            b.QuestType,
            b.StatType,
            b.Difficulty,
            Progression.TargetLabel(probe, b.StartValue),
            Progression.TargetLabel(probe, b.EndValue),
            b.StageCount,
            b.DaysPerStep,
            stepLabel);
    }

    /// <summary>
    /// 組出目標清單；排序依 <see cref="Goal.CreatedAt"/>，同一請求內建立、時間戳相同的目標再依其任務最小 <see cref="Quest.SortOrder"/> 排序（任務建立時依請求順序嚴格遞增），還原建立順序。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="context">今日內容，提供各任務的達標天數。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>目標清單。</returns>
    private async Task<GoalsResponse> BuildListAsync(Guid userId, TodayContext context, CancellationToken ct)
    {
        var goals = await db.Goals.Where(g => g.UserId == userId && !g.IsArchived).ToListAsync(ct);
        var goalIds = goals.Select(g => g.Id).ToHashSet();
        var quests = await db.Quests.Where(q => q.GoalId != null && goalIds.Contains(q.GoalId.Value)).OrderBy(q => q.SortOrder).ToListAsync(ct);
        var byGoal = quests.ToLookup(q => q.GoalId!.Value);
        var orderedGoals = goals
            .OrderBy(g => g.CreatedAt)
            .ThenBy(g => byGoal[g.Id].Any() ? byGoal[g.Id].Min(q => q.SortOrder) : int.MaxValue)
            .ToList();

        return new GoalsResponse(orderedGoals.Select(g => new GoalDto(
            g.Id,
            g.Category,
            GoalCategories.Get(g.Category).Title,
            g.LengthDays,
            g.StartDate,
            byGoal[g.Id].Select(q =>
            {
                var days = context.DoneDaysOf(q.Id);
                var target = Progression.EffectiveTarget(q, days) ?? 0;
                return new GoalQuestDto(q.Id, Progression.RenderName(q, days), Progression.StageOf(q, days), q.StageCount ?? 1, Progression.TargetLabel(q, target), q.IsArchived);
            }).ToList())).ToList());
    }
}
