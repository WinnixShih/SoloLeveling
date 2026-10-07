namespace SoloLeveling.Domain;

/// <summary>
/// 成就判定所需的統計快照，由 Api 層批次查詢後組成。
/// </summary>
/// <param name="GoalCount">建立過的目標數（含已封存）。</param>
/// <param name="BestStreak">最佳連續達標天數。</param>
/// <param name="TotalCompleted">累計完成任務次數。</param>
/// <param name="RoleStreaks">各角色任務的連續達標天數（只算未封存的角色任務）。</param>
/// <param name="RoleMinutes">各角色任務的累計分鐘（只有運動、閱讀；含已封存任務）。</param>
/// <param name="OwnedCardKinds">圖鑑擁有的卡片種數。</param>
/// <param name="CompletedPrograms">已完成的 66 天週期數。</param>
/// <param name="PeakLevel">曾到達的最高等級。</param>
public sealed record AchievementStats(
    int GoalCount,
    int BestStreak,
    int TotalCompleted,
    IReadOnlyDictionary<QuestRole, int> RoleStreaks,
    IReadOnlyDictionary<QuestRole, int> RoleMinutes,
    int OwnedCardKinds,
    int CompletedPrograms,
    int PeakLevel)
{
    /// <summary>全部為 0、等級 1 的統計（新玩家）。</summary>
    public static AchievementStats Empty { get; } = new(0, 0, 0, new Dictionary<QuestRole, int>(), new Dictionary<QuestRole, int>(), 0, 0, 1);

    /// <summary>
    /// 某角色的連續達標天數。
    /// </summary>
    /// <param name="role">角色。</param>
    /// <returns>天數；沒有該角色任務為 0。</returns>
    public int StreakOf(QuestRole role)
    {
        return RoleStreaks.TryGetValue(role, out var days) ? days : 0;
    }

    /// <summary>
    /// 某角色的累計分鐘。
    /// </summary>
    /// <param name="role">角色。</param>
    /// <returns>分鐘；沒有紀錄為 0。</returns>
    public int MinutesOf(QuestRole role)
    {
        return RoleMinutes.TryGetValue(role, out var minutes) ? minutes : 0;
    }
}

/// <summary>
/// 一個成就：條件達到門檻即解鎖（一次性），解鎖一個稱號字塊。
/// </summary>
/// <param name="Key">kebab-case 鍵，同時是稱號字塊鍵。</param>
/// <param name="Name">成就名稱。</param>
/// <param name="Condition">條件說明（顯示用）。</param>
/// <param name="TitleText">解鎖的字塊文字。</param>
/// <param name="Slot">字塊位置。</param>
/// <param name="Target">門檻。</param>
/// <param name="Measure">從統計取出目前值。</param>
public sealed record AchievementDefinition(string Key, string Name, string Condition, string TitleText, TitleSlot Slot, int Target, Func<AchievementStats, int> Measure)
{
    /// <summary>
    /// 是否達到門檻。
    /// </summary>
    /// <param name="stats">統計。</param>
    /// <returns>目前值 &gt;= 門檻。</returns>
    public bool IsMet(AchievementStats stats)
    {
        return Measure(stats) >= Target;
    }

    /// <summary>
    /// 顯示用進度，不超過門檻。
    /// </summary>
    /// <param name="stats">統計。</param>
    /// <returns>0 到 <see cref="Target"/>。</returns>
    public int ProgressOf(AchievementStats stats)
    {
        return Math.Min(Target, Measure(stats));
    }
}

/// <summary>
/// 全部成就；順序即前端顯示順序。
/// </summary>
public static class Achievements
{
    /// <summary>「不屈」的鍵；首次解鎖時另發 C 級寶箱。</summary>
    public const string Streak7Key = "streak-7";

    /// <summary>「恆心」的鍵；首次解鎖時另發 A 級寶箱。</summary>
    public const string Streak30Key = "streak-30";

    /// <summary>到達 S 階的等級，須與 <c>Leveling.RankOf</c> 的分界一致。</summary>
    public const int SRankLevel = 45;

    /// <summary>第一批 12 個成就。</summary>
    public static IReadOnlyList<AchievementDefinition> All { get; } =
    [
        new("first-goal", "初次覺醒", "建立第一個目標", "覺醒的", TitleSlot.Prefix, 1, s => s.GoalCount),
        new(Streak7Key, "不屈", "最佳連續 7 天", "不屈的", TitleSlot.Prefix, 7, s => s.BestStreak),
        new(Streak30Key, "恆心", "最佳連續 30 天", "恆心的", TitleSlot.Prefix, 30, s => s.BestStreak),
        new("quests-100", "百戰", "累計完成 100 個任務", "百戰獵人", TitleSlot.Suffix, 100, s => s.TotalCompleted),
        new("wake-30", "晨曦", "起床任務連續達標 30 天", "晨曦騎士", TitleSlot.Suffix, 30, s => s.StreakOf(QuestRole.WakeUp)),
        new("bed-30", "靜夜", "就寢任務連續達標 30 天", "靜夜的", TitleSlot.Prefix, 30, s => s.StreakOf(QuestRole.Bedtime)),
        new("screen-30", "手機克星", "螢幕時間任務連續達標 30 天", "手機克星", TitleSlot.Suffix, 30, s => s.StreakOf(QuestRole.ScreenTime)),
        new("exercise-1000", "百里", "運動任務累計 1000 分鐘", "百里行者", TitleSlot.Suffix, 1000, s => s.MinutesOf(QuestRole.Exercise)),
        new("reading-1000", "藏書", "閱讀任務累計 1000 分鐘", "藏書者", TitleSlot.Suffix, 1000, s => s.MinutesOf(QuestRole.Reading)),
        new("program-complete", "破繭", "完成一個 66 天週期", "破繭的", TitleSlot.Prefix, 1, s => s.CompletedPrograms),
        new("collector-10", "收藏家", "圖鑑擁有 10 種卡片", "收藏家", TitleSlot.Suffix, 10, s => s.OwnedCardKinds),
        new("rank-s", "傳說", "到達 S 階", "傳說的", TitleSlot.Prefix, SRankLevel, s => s.PeakLevel),
    ];

    private static readonly Dictionary<string, AchievementDefinition> Index = All.ToDictionary(a => a.Key);

    /// <summary>
    /// 依鍵取成就。
    /// </summary>
    /// <param name="key">成就鍵。</param>
    /// <returns>成就；不存在回 null。</returns>
    public static AchievementDefinition? Find(string key)
    {
        return Index.GetValueOrDefault(key);
    }
}
