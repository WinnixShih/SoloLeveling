using SoloLeveling.Domain.Entities;

namespace SoloLeveling.Domain.Rules;

/// <summary>
/// 請求開始時（結算後、任何修改前）的玩家快照；只用來算回應的 <see cref="RewardOutcome.LevelsGained"/>。
/// </summary>
/// <param name="Level">當時的等級。</param>
public sealed record PlayerSnapshot(int Level);

/// <summary>
/// 要建立的寶箱。
/// </summary>
/// <param name="Rarity">等級。</param>
/// <param name="Source">來源。</param>
public sealed record ChestGrant(Rarity Rarity, ChestSource Source);

/// <summary>
/// 要寫入的金幣變動（由呼叫端經 <see cref="Wallet.Change"/> 套用）。
/// </summary>
/// <param name="Amount">變動量；收回為負。</param>
/// <param name="Source">來源。</param>
public sealed record CoinGrant(int Amount, CoinSource Source);

/// <summary>
/// 獎勵判定的輸入。
/// </summary>
/// <param name="Before">請求開始時的快照。</param>
/// <param name="Player">本次修改後的玩家。</param>
/// <param name="TodayLog">本次修改後的今日紀錄。</param>
/// <param name="UnlockedKeys">已解鎖的成就鍵。</param>
/// <param name="Stats">成就統計（反映本次修改）。</param>
/// <param name="CompletedGoals">本次請求新完成的目標數。</param>
/// <param name="CompletedPrograms">本次請求新完成的 66 天週期數。</param>
public sealed record RewardInput(
    PlayerSnapshot Before,
    Player Player,
    DailyLog TodayLog,
    IReadOnlySet<string> UnlockedKeys,
    AchievementStats Stats,
    int CompletedGoals,
    int CompletedPrograms);

/// <summary>
/// 獎勵判定的結果；由呼叫端套用到實體並持久化。
/// </summary>
/// <param name="LevelsGained">相對請求開始時升了幾級（降級為 0），僅供顯示。</param>
/// <param name="NewPeakLevel">更新後的最高等級。</param>
/// <param name="RankUps">新晉升到的階級代碼，依序。</param>
/// <param name="Chests">要建立的寶箱，依序：升級 E、晉階 C、連續 7／30、目標、週期。</param>
/// <param name="NewAchievements">新解鎖的成就。</param>
/// <param name="Coins">金幣變動：先達標金幣，再成就金幣。</param>
/// <param name="ShieldsGained">要加的保險卡張數（已受上限截斷）。</param>
/// <param name="ClearCoinsGranted">今日紀錄的 <see cref="DailyLog.ClearCoinsGranted"/> 新值。</param>
public sealed record RewardOutcome(
    int LevelsGained,
    int NewPeakLevel,
    IReadOnlyList<string> RankUps,
    IReadOnlyList<ChestGrant> Chests,
    IReadOnlyList<AchievementDefinition> NewAchievements,
    IReadOnlyList<CoinGrant> Coins,
    int ShieldsGained,
    bool ClearCoinsGranted);

/// <summary>
/// 獎勵判定（純函式，不修改傳入的實體）。升級與晉階以 <see cref="Player.PeakLevel"/> 判定，撤銷降級後再升回來不重發；
/// 成就以「條件成立且未解鎖」判定，因此排程結算造成的狀態變化也會在下一次請求補上。
/// </summary>
public static class Rewards
{
    /// <summary>今日首次達標的金幣。</summary>
    public const int DailyClearCoins = 10;

    /// <summary>解鎖一個成就的金幣。</summary>
    public const int AchievementCoins = 50;

    /// <summary>
    /// 判定本次請求的獎勵。
    /// </summary>
    /// <param name="input">輸入。</param>
    /// <returns>結果。</returns>
    public static RewardOutcome Evaluate(RewardInput input)
    {
        var player = input.Player;
        var chests = new List<ChestGrant>();
        var rankUps = new List<string>();

        var newPeak = Math.Max(player.PeakLevel, player.Level);
        for (var level = player.PeakLevel + 1; level <= newPeak; level++)
        {
            chests.Add(new ChestGrant(Rarity.E, ChestSource.LevelUp));
            var rank = Leveling.RankOf(level).Rank;
            if (rank != Leveling.RankOf(level - 1).Rank)
            {
                rankUps.Add(rank);
            }
        }

        chests.AddRange(rankUps.Select(_ => new ChestGrant(Rarity.C, ChestSource.RankUp)));

        var newAchievements = Achievements.All
            .Where(a => !input.UnlockedKeys.Contains(a.Key) && a.IsMet(input.Stats))
            .ToList();
        if (newAchievements.Any(a => a.Key == Achievements.Streak7Key))
        {
            chests.Add(new ChestGrant(Rarity.C, ChestSource.Streak7));
        }

        if (newAchievements.Any(a => a.Key == Achievements.Streak30Key))
        {
            chests.Add(new ChestGrant(Rarity.A, ChestSource.Streak30));
        }

        chests.AddRange(Enumerable.Repeat(new ChestGrant(Rarity.A, ChestSource.GoalCompleted), input.CompletedGoals));
        chests.AddRange(Enumerable.Repeat(new ChestGrant(Rarity.S, ChestSource.ProgramCompleted), input.CompletedPrograms));

        var coins = new List<CoinGrant>();
        var log = input.TodayLog;
        var clearCoinsGranted = log.ClearCoinsGranted;
        if (log.BonusGranted && !log.ClearCoinsGranted)
        {
            coins.Add(new CoinGrant(DailyClearCoins, CoinSource.DailyClear));
            clearCoinsGranted = true;
        }
        else if (!log.BonusGranted && log.ClearCoinsGranted)
        {
            // 達標金幣可能已被花掉，最多收回到 0，餘額不為負
            var refund = Math.Min(DailyClearCoins, player.Coins);
            if (refund > 0)
            {
                coins.Add(new CoinGrant(-refund, CoinSource.DailyClearUndo));
            }

            clearCoinsGranted = false;
        }

        coins.AddRange(newAchievements.Select(_ => new CoinGrant(AchievementCoins, CoinSource.Achievement)));

        var shieldsGained = Math.Max(0, Math.Min(rankUps.Count, Shop.MaxShields - player.ShieldCount));
        return new RewardOutcome(
            Math.Max(0, player.Level - input.Before.Level),
            newPeak,
            rankUps,
            chests,
            newAchievements,
            coins,
            shieldsGained,
            clearCoinsGranted);
    }
}
