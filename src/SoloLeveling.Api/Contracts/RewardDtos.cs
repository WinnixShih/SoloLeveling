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

/// <summary>成就清單項目。</summary>
/// <param name="Key">成就鍵（同稱號字塊鍵）。</param>
/// <param name="Name">名稱。</param>
/// <param name="Condition">條件說明。</param>
/// <param name="TitleText">解鎖的字塊文字。</param>
/// <param name="Slot">字塊位置。</param>
/// <param name="Unlocked">是否已解鎖。</param>
/// <param name="Progress">目前進度；已解鎖時等於 <paramref name="Target"/>。</param>
/// <param name="Target">門檻。</param>
/// <param name="UnlockedAt">解鎖時間（Unix 秒）；未解鎖為 null。</param>
public record AchievementDto(string Key, string Name, string Condition, string TitleText, TitleSlot Slot, bool Unlocked, int Progress, int Target, long? UnlockedAt);

/// <summary>已解鎖、可選用的稱號字塊。</summary>
/// <param name="Key">字塊鍵（成就鍵）。</param>
/// <param name="Text">字塊文字。</param>
/// <param name="Slot">字塊位置。</param>
public record TitleFragmentDto(string Key, string Text, TitleSlot Slot);

/// <summary>GET /rewards：檔案頁需要的獎勵總覽。</summary>
/// <param name="Coins">金幣餘額。</param>
/// <param name="ShieldCount">連勝保險卡張數。</param>
/// <param name="UnopenedChests">未開寶箱，舊到新。</param>
/// <param name="Achievements">全部成就與進度，依目錄順序。</param>
/// <param name="TitleFragments">已解鎖的字塊，依目錄順序。</param>
/// <param name="TitlePrefixKey">目前選的前綴；未選為 null。</param>
/// <param name="TitleSuffixKey">目前選的後綴；未選為 null。</param>
/// <param name="Title">組合後的稱號。</param>
/// <param name="PinnedCard">釘選的卡片；未釘選為 null。</param>
/// <param name="ThemeKey">目前主題。</param>
/// <param name="OwnedThemes">可使用的主題（含預設 azure），依目錄順序。</param>
public record RewardsResponse(
    int Coins,
    int ShieldCount,
    List<ChestDto> UnopenedChests,
    List<AchievementDto> Achievements,
    List<TitleFragmentDto> TitleFragments,
    string? TitlePrefixKey,
    string? TitleSuffixKey,
    string Title,
    CardDto? PinnedCard,
    string ThemeKey,
    List<string> OwnedThemes);

/// <summary>開箱結果。</summary>
/// <param name="Card">抽到的卡片。</param>
/// <param name="IsDuplicate">是否為重複卡。</param>
/// <param name="Coins">本次開箱得到的金幣（開箱＋重複卡）。</param>
/// <param name="CoinBalance">開箱與獎勵套用後的金幣餘額。</param>
/// <param name="Rewards">本次獎勵（例如湊滿 10 種卡解鎖收藏家）。</param>
public record OpenChestResponse(CardDto Card, bool IsDuplicate, int Coins, int CoinBalance, RewardsDto Rewards);

/// <summary>圖鑑項目。</summary>
/// <param name="Id">卡片 ID。</param>
/// <param name="Name">名稱。</param>
/// <param name="Rarity">稀有度。</param>
/// <param name="Flavor">風味文字。</param>
/// <param name="Image">插畫相對路徑。</param>
/// <param name="Owned">是否擁有；未擁有時前端以剪影與「？」顯示。</param>
/// <param name="Count">持有張數；未擁有為 0。</param>
/// <param name="FirstAcquiredAt">首次取得時間（Unix 秒）；未擁有為 null。</param>
public record CardEntryDto(string Id, string Name, Rarity Rarity, string Flavor, string Image, bool Owned, int Count, long? FirstAcquiredAt);

/// <summary>GET /cards。</summary>
/// <param name="Cards">全部卡片，依目錄順序。</param>
/// <param name="OwnedKinds">擁有的種數。</param>
/// <param name="Total">目錄總數。</param>
public record CardsResponse(List<CardEntryDto> Cards, int OwnedKinds, int Total);

/// <summary>PUT /me/title。</summary>
/// <param name="PrefixKey">前綴字塊鍵；null 表示不選。</param>
/// <param name="SuffixKey">後綴字塊鍵；null 表示不選。</param>
public record SetTitleRequest(string? PrefixKey, string? SuffixKey);

/// <summary>PUT /me/pinned-card。</summary>
/// <param name="CardId">卡片 ID；null 表示取消釘選。</param>
public record SetPinnedCardRequest(string? CardId);

/// <summary>PUT /me/theme。</summary>
/// <param name="ThemeKey">主題鍵。</param>
public record SetThemeRequest(string ThemeKey);

/// <summary>POST /shop/purchase。</summary>
/// <param name="Item">商品。</param>
/// <param name="ThemeKey">商品為 Theme 時必填。</param>
public record PurchaseRequest(ShopItem Item, string? ThemeKey);

/// <summary>購買結果。</summary>
/// <param name="Coins">扣款後的金幣餘額。</param>
/// <param name="ShieldCount">目前保險卡張數。</param>
/// <param name="OwnedThemes">可使用的主題（含預設 azure）。</param>
/// <param name="Chest">購買 E 級寶箱時為新寶箱，其他為 null。</param>
/// <param name="Rewards">本次獎勵。</param>
public record PurchaseResponse(int Coins, int ShieldCount, List<string> OwnedThemes, ChestDto? Chest, RewardsDto Rewards);
