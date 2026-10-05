using SoloLeveling.Domain;

namespace SoloLeveling.Api.Contracts;

/// <summary>寶箱。</summary>
/// <param name="Id">寶箱 ID。</param>
/// <param name="Rarity">等級（E／C／A／S）。</param>
/// <param name="Source">取得來源。</param>
/// <param name="CreatedAt">取得時間（Unix 秒）。</param>
public record ChestDto(Guid Id, Rarity Rarity, ChestSource Source, long CreatedAt);

/// <summary>卡片。</summary>
/// <param name="Id">卡片 ID。</param>
/// <param name="Name">名稱。</param>
/// <param name="Rarity">稀有度。</param>
/// <param name="Flavor">風味文字。</param>
/// <param name="Image">插畫相對路徑；檔案可能尚未存在，前端需顯示佔位卡。</param>
public record CardDto(string Id, string Name, Rarity Rarity, string Flavor, string Image);

/// <summary>本次新解鎖的成就。</summary>
/// <param name="Key">成就鍵（同稱號字塊鍵）。</param>
/// <param name="Name">成就名稱。</param>
/// <param name="TitleText">解鎖的字塊文字。</param>
/// <param name="Slot">字塊位置。</param>
public record UnlockedAchievementDto(string Key, string Name, string TitleText, TitleSlot Slot);

/// <summary>本次請求產生的獎勵；前端依序顯示系統訊息：升級 → 晉階 → 寶箱 → 成就 → 保險卡生效。</summary>
/// <param name="LevelsGained">相對請求開始時升了幾級（降級為 0）。</param>
/// <param name="RankUps">新晉升到的階級代碼，依序。</param>
/// <param name="NewChests">本次獲得的寶箱。</param>
/// <param name="NewAchievements">本次解鎖的成就。</param>
/// <param name="CoinDelta">本次自動發放／收回的金幣淨額（達標、成就）；開箱與商店的金幣在各自的回應欄位。</param>
/// <param name="ShieldsGained">本次因晉階獲得的保險卡張數。</param>
/// <param name="ShieldsUsed">尚未回報過的保險卡生效日期（可能來自排程結算），由舊到新。</param>
/// <param name="ShieldCount">目前持有的保險卡張數。</param>
public record RewardsDto(
    int LevelsGained,
    List<string> RankUps,
    List<ChestDto> NewChests,
    List<UnlockedAchievementDto> NewAchievements,
    int CoinDelta,
    int ShieldsGained,
    List<DateOnly> ShieldsUsed,
    int ShieldCount);

/// <summary>沒有其他內容的寫入回應（封存）只帶本次獎勵。</summary>
/// <param name="Rewards">本次獎勵。</param>
public record RewardsEnvelope(RewardsDto Rewards);
