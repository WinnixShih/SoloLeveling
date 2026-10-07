using System.Text.Json.Serialization;
using SoloLeveling.Domain;

namespace SoloLeveling.Api.Contracts;

/// <summary>今日任務與進度。</summary>
/// <param name="Date">今日（使用者時區）。</param>
/// <param name="CompletionRatio">達標率（0–1）。</param>
/// <param name="IsCleared">是否達標。</param>
/// <param name="Threshold">目前模式的達標門檻。</param>
/// <param name="BonusGranted">是否已發達標獎勵。</param>
/// <param name="Note">今日反思。</param>
/// <param name="Quests">任務與進度。</param>
/// <param name="Rewards">本次請求的獎勵。</param>
public record TodayResponse(
    DateOnly Date,
    decimal CompletionRatio,
    bool IsCleared,
    decimal Threshold,
    bool BonusGranted,
    string? Note,
    List<TodayQuestDto> Quests,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RewardsDto? Rewards = null);

/// <summary>今日的單一任務與進度。</summary>
/// <param name="Id">任務 ID。</param>
/// <param name="Name">名稱。</param>
/// <param name="StatType">屬性代碼。</param>
/// <param name="Difficulty">難度。</param>
/// <param name="QuestType">任務類型。</param>
/// <param name="TargetValue">目標值。</param>
/// <param name="Step">增減量。</param>
/// <param name="Unit">單位。</param>
/// <param name="SortOrder">顯示順序。</param>
/// <param name="Value">今日進度值；null 表示未填。</param>
/// <param name="IsDone">今日是否完成。</param>
/// <param name="XpReward">完成可得 EXP。</param>
/// <param name="StatReward">完成可得屬性點。</param>
/// <param name="Progression">漸進任務的階段資訊；一般任務為 null。</param>
public record TodayQuestDto(
    Guid Id,
    string Name,
    StatType StatType,
    Difficulty Difficulty,
    QuestType QuestType,
    decimal? TargetValue,
    decimal? Step,
    string? Unit,
    int SortOrder,
    decimal? Value,
    bool IsDone,
    int XpReward,
    int StatReward,
    ProgressionDto? Progression);

/// <summary>漸進任務的階段資訊；一般任務為 null。</summary>
/// <param name="GoalId">所屬目標。</param>
/// <param name="Stage">目前階段（從 1 起）。</param>
/// <param name="StageCount">總階數。</param>
/// <param name="TargetLabel">今日目標的顯示文字。</param>
public record ProgressionDto(Guid GoalId, int Stage, int StageCount, string TargetLabel);

/// <summary>寫入進度請求。</summary>
/// <param name="Value">新值；null 表示清空（Limit 類型的「未填」）。</param>
public record ProgressRequest(decimal? Value);

/// <summary>今日反思請求。</summary>
/// <param name="Note">內容，最長 2000 字。</param>
public record NoteRequest(string Note);

/// <summary>某一天的紀錄。</summary>
/// <param name="Date">日期。</param>
/// <param name="CompletionRatio">達標率。</param>
/// <param name="IsCleared">是否達標。</param>
/// <param name="DoneQuestIds">當天完成的任務 ID。</param>
/// <param name="Note">反思。</param>
public record HistoryDayDto(DateOnly Date, decimal CompletionRatio, bool IsCleared, List<Guid> DoneQuestIds, string? Note);

/// <summary>EXP 流水。</summary>
/// <param name="Amount">變動量。</param>
/// <param name="Source">來源。</param>
/// <param name="RefId">關聯的任務或紀錄 ID。</param>
/// <param name="OccurredAt">發生時間（Unix 秒）。</param>
public record XpEventDto(int Amount, XpSource Source, Guid? RefId, long OccurredAt);
