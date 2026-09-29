using System.Text.Json;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Goals;

namespace SoloLeveling.Api.Contracts;

/// <summary>引導問題。</summary>
/// <param name="Key">回答的鍵。</param>
/// <param name="Label">顯示文字。</param>
/// <param name="Type">回答型別（Time／Integer／Decimal）。</param>
/// <param name="Min">最小值。</param>
/// <param name="Max">最大值。</param>
/// <param name="Default">預設值。</param>
public record GoalQuestionDto(string Key, string Label, GoalQuestionType Type, decimal? Min, decimal? Max, decimal? Default);

/// <summary>目標類別定義。</summary>
/// <param name="Category">類別。</param>
/// <param name="Title">顯示名稱。</param>
/// <param name="Questions">問題清單。</param>
/// <param name="ReplacesBasicQuestIndexes">會取代的基本任務索引。</param>
public record GoalCategoryDto(GoalCategory Category, string Title, List<GoalQuestionDto> Questions, List<int> ReplacesBasicQuestIndexes);

/// <summary>基本任務（原預設任務）。</summary>
/// <param name="Index">索引，對應 basicQuestIndexes。</param>
/// <param name="Name">名稱。</param>
/// <param name="StatType">屬性代碼。</param>
/// <param name="Difficulty">難度。</param>
public record BasicQuestDto(int Index, string Name, StatType StatType, Difficulty Difficulty);

/// <summary>GET /goals/categories。</summary>
/// <param name="Categories">類別定義。</param>
/// <param name="BasicQuests">基本任務。</param>
public record CategoriesResponse(List<GoalCategoryDto> Categories, List<BasicQuestDto> BasicQuests);

/// <summary>一個目標的輸入。</summary>
/// <param name="Category">類別。</param>
/// <param name="Answers">回答；值可為字串或數字，統一轉成字串交給 Domain 解析。</param>
public record GoalInput(GoalCategory Category, Dictionary<string, JsonElement> Answers)
{
    /// <summary>
    /// 把回答的 JSON 值轉成字串字典（數字取原始文字、字串取內容）。
    /// </summary>
    /// <returns>字串字典。</returns>
    public IReadOnlyDictionary<string, string> AnswersAsStrings()
    {
        return Answers.ToDictionary(kv => kv.Key, kv => kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() ?? string.Empty : kv.Value.GetRawText());
    }
}

/// <summary>POST /goals 與 POST /goals/preview 的輸入。</summary>
/// <param name="Goals">目標，至少一個，類別不可重複。</param>
/// <param name="BasicQuestIndexes">要一併加入的基本任務索引；可為 null 或空。</param>
public record CreateGoalsRequest(List<GoalInput> Goals, List<int>? BasicQuestIndexes);

/// <summary>預覽中的單一任務。</summary>
/// <param name="Name">第 1 階的顯示名稱。</param>
/// <param name="QuestType">任務類型。</param>
/// <param name="StatType">屬性代碼。</param>
/// <param name="Difficulty">難度。</param>
/// <param name="StartLabel">起點顯示文字。</param>
/// <param name="EndLabel">終點顯示文字。</param>
/// <param name="StageCount">總階數。</param>
/// <param name="DaysPerStep">每階達標天數。</param>
/// <param name="StepLabel">每階變化的顯示文字，例如「提早 5 分鐘」。</param>
public record PreviewQuestDto(string Name, QuestType QuestType, StatType StatType, Difficulty Difficulty, string StartLabel, string EndLabel, int StageCount, int DaysPerStep, string StepLabel);

/// <summary>預覽中的單一目標。</summary>
/// <param name="Category">類別。</param>
/// <param name="Title">顯示名稱。</param>
/// <param name="Quests">會產生的任務。</param>
public record GoalPreviewDto(GoalCategory Category, string Title, List<PreviewQuestDto> Quests);

/// <summary>POST /goals/preview。</summary>
/// <param name="Goals">預覽結果。</param>
public record PreviewResponse(List<GoalPreviewDto> Goals);

/// <summary>目標底下的任務。</summary>
/// <param name="Id">任務 ID。</param>
/// <param name="Name">今日的顯示名稱。</param>
/// <param name="Stage">目前階段。</param>
/// <param name="StageCount">總階數。</param>
/// <param name="TargetLabel">今日目標的顯示文字。</param>
/// <param name="IsArchived">是否已單獨封存。</param>
public record GoalQuestDto(Guid Id, string Name, int Stage, int StageCount, string TargetLabel, bool IsArchived);

/// <summary>目標。</summary>
/// <param name="Id">目標 ID。</param>
/// <param name="Category">類別。</param>
/// <param name="Title">顯示名稱。</param>
/// <param name="LengthDays">目標天數。</param>
/// <param name="StartDate">開始日。</param>
/// <param name="Quests">任務。</param>
public record GoalDto(Guid Id, GoalCategory Category, string Title, int LengthDays, DateOnly StartDate, List<GoalQuestDto> Quests);

/// <summary>GET /goals 與 POST /goals 的回應。</summary>
/// <param name="Goals">進行中的目標。</param>
public record GoalsResponse(List<GoalDto> Goals);
