namespace SoloLeveling.Domain.Goals;

/// <summary>
/// 規劃器算出的漸進任務藍圖；Api 依此建立 <see cref="Entities.Quest"/>。
/// </summary>
/// <param name="Name">名稱；時間類含 <c>{target}</c> 佔位。</param>
/// <param name="StatType">屬性。</param>
/// <param name="Difficulty">難度。</param>
/// <param name="QuestType">任務類型。</param>
/// <param name="ValueKind">目標值種類。</param>
/// <param name="StartValue">起點。</param>
/// <param name="EndValue">終點。</param>
/// <param name="StepValue">每階變化量。</param>
/// <param name="StageCount">總階數。</param>
/// <param name="DaysPerStep">每階達標天數。</param>
/// <param name="UiStep">前端 −／＋ 的增減量；Check 為 null。</param>
/// <param name="Unit">單位；Check 為 null。</param>
public record GoalQuestBlueprint(
    string Name,
    StatType StatType,
    Difficulty Difficulty,
    QuestType QuestType,
    ProgressionValueKind ValueKind,
    decimal StartValue,
    decimal EndValue,
    decimal StepValue,
    int StageCount,
    int DaysPerStep,
    decimal? UiStep,
    string? Unit);
