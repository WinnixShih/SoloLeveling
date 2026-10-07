using Microsoft.EntityFrameworkCore;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Errors;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Services;

/// <summary>
/// 獎勵總覽、開箱、圖鑑與商店。開箱與購買照「結算 → 修改 → 存檔 → 套用獎勵 → commit」，Player 列鎖讓同一使用者的開箱與購買依序執行。
/// </summary>
/// <param name="db">DbContext。</param>
/// <param name="loader">今日內容載入（含結算與列鎖）。</param>
/// <param name="statsLoader">成就統計載入。</param>
/// <param name="rewardApplier">獎勵判定與持久化。</param>
/// <param name="clock">時間來源。</param>
/// <param name="rng">抽卡亂數。</param>
public class RewardService(
    AppDbContext db,
    TodayContextLoader loader,
    RewardStatsLoader statsLoader,
    RewardApplier rewardApplier,
    TimeProvider clock,
    Random rng)
{
    /// <summary>
    /// GET /rewards：金幣、保險卡、未開寶箱、成就與進度、稱號字塊與目前組合、釘選卡、主題。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>總覽。</returns>
    public async Task<RewardsResponse> GetAsync(Guid userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // 只結算、不套用獎勵：套用會把待公告的保險卡事件標成已公告，而這支回應沒有 rewards 欄位
        var context = await loader.LoadAsync(userId, ct);
        var state = await statsLoader.LoadAsync(context.Player, context.Today, ct);
        var unlockedAt = await db.Achievements.Where(a => a.UserId == userId).ToDictionaryAsync(a => a.Key, a => a.UnlockedAt, ct);
        var chests = await db.RewardChests
            .Where(c => c.UserId == userId && c.OpenedAt == null)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);
        var themes = await OwnedThemesAsync(userId, ct);
        await tx.CommitAsync(ct);

        var player = context.Player;
        var achievements = Achievements.All.Select(a =>
        {
            var unlocked = unlockedAt.TryGetValue(a.Key, out var at);
            return new AchievementDto(
                a.Key, a.Name, a.Condition, a.TitleText, a.Slot, unlocked,
                unlocked ? a.Target : a.ProgressOf(state.Stats),
                a.Target,
                unlocked ? at.ToUnixSeconds() : (long?)null);
        }).ToList();
        var fragments = Achievements.All
            .Where(a => unlockedAt.ContainsKey(a.Key))
            .Select(a => new TitleFragmentDto(a.Key, a.TitleText, a.Slot))
            .ToList();
        var pinned = player.PinnedCardId is { } cardId ? Cards.Find(cardId)?.ToDto() : null;
        return new RewardsResponse(
            player.Coins,
            player.ShieldCount,
            chests.Select(c => c.ToDto()).ToList(),
            achievements,
            fragments,
            player.TitlePrefixKey,
            player.TitleSuffixKey,
            Titles.Compose(player.TitlePrefixKey, player.TitleSuffixKey, player.Level),
            pinned,
            player.ThemeKey,
            themes);
    }

    /// <summary>
    /// GET /cards：目錄加擁有狀態與張數；不需要「今日」，不結算。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>圖鑑。</returns>
    public async Task<CardsResponse> GetCardsAsync(Guid userId, CancellationToken ct)
    {
        var owned = await db.OwnedCards.AsNoTracking().Where(c => c.UserId == userId).ToDictionaryAsync(c => c.CardId, ct);
        var cards = Cards.All.Select(c => owned.TryGetValue(c.Id, out var o)
            ? new CardEntryDto(c.Id, c.Name, c.Rarity, c.Flavor, c.Image, true, o.Count, o.FirstAcquiredAt.ToUnixSeconds())
            : new CardEntryDto(c.Id, c.Name, c.Rarity, c.Flavor, c.Image, false, 0, null)).ToList();
        return new CardsResponse(cards, owned.Count, Cards.All.Count);
    }

    /// <summary>
    /// POST /rewards/chests/{id}/open：抽一張卡加入收藏（重複則加張數並轉金幣），另附開箱金幣。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="chestId">寶箱 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>開箱結果。</returns>
    /// <exception cref="ApiErrorException">寶箱不存在、已開啟或不屬於此使用者（404，<c>ChestNotFound</c>）。</exception>
    public async Task<OpenChestResponse> OpenChestAsync(Guid userId, Guid chestId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // loader 會鎖 Player 列：同一寶箱的併發開啟，第二個請求要等第一個 commit 後才查得到 OpenedAt 而回 404
        var context = await loader.LoadAsync(userId, ct);
        var chest = await db.RewardChests.SingleOrDefaultAsync(c => c.Id == chestId && c.UserId == userId && c.OpenedAt == null, ct)
            ?? throw ApiErrorException.NotFound("ChestNotFound", "寶箱不存在或已開啟");
        var owned = await db.OwnedCards.Where(c => c.UserId == userId).ToListAsync(ct);
        var loot = Loot.Open(chest.Rarity, owned.ToDictionary(c => c.CardId, c => c.Count), rng);
        var now = clock.GetUtcNow();

        var card = owned.SingleOrDefault(c => c.CardId == loot.CardId);
        if (card is null)
        {
            db.OwnedCards.Add(new OwnedCard { UserId = userId, CardId = loot.CardId, Count = 1, FirstAcquiredAt = now });
        }
        else
        {
            card.Count += 1;
        }

        chest.OpenedAt = now;
        chest.DroppedCardId = loot.CardId;
        chest.Coins = loot.Coins;
        db.CoinEvents.Add(Wallet.Change(context.Player, loot.BaseCoins, CoinSource.ChestOpen, chest.Id, now));
        if (loot.DuplicateCoins > 0)
        {
            db.CoinEvents.Add(Wallet.Change(context.Player, loot.DuplicateCoins, CoinSource.DuplicateCard, chest.Id, now));
        }

        await db.SaveChangesAsync(ct);
        var rewards = await rewardApplier.ApplyAsync(context, 0, ct);
        await tx.CommitAsync(ct);
        return new OpenChestResponse(Cards.Find(loot.CardId)!.ToDto(), loot.IsDuplicate, loot.Coins, context.Player.Coins, rewards);
    }

    /// <summary>
    /// POST /shop/purchase：連勝保險卡、E 級寶箱或主題。先檢查上限與擁有狀態，再檢查金幣。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="request">商品與主題鍵。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>購買結果。</returns>
    /// <exception cref="ApiErrorException">保險卡已滿（<c>ShieldLimitReached</c>）、主題已擁有（<c>ThemeOwned</c>）、主題不存在（<c>UnknownTheme</c>）或未知商品（<c>UnknownItem</c>），皆 400。</exception>
    /// <exception cref="DomainValidationException">金幣不足（400，<c>NotEnoughCoins</c>）。</exception>
    public async Task<PurchaseResponse> PurchaseAsync(Guid userId, PurchaseRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        var player = context.Player;
        var now = clock.GetUtcNow();
        ChestDto? chestDto = null;

        if (request.Item == ShopItem.Shield)
        {
            if (player.ShieldCount >= Shop.MaxShields)
            {
                throw ApiErrorException.BadRequest("ShieldLimitReached", $"連勝保險卡最多持有 {Shop.MaxShields} 張");
            }

            db.CoinEvents.Add(Wallet.Spend(player, Shop.ShieldPrice, CoinSource.ShopShield, null, now));
            player.ShieldCount += 1;
        }
        else if (request.Item == ShopItem.EChest)
        {
            var chest = new RewardChest { Id = Guid.NewGuid(), UserId = userId, Rarity = Rarity.E, Source = ChestSource.Purchase, CreatedAt = now };
            db.CoinEvents.Add(Wallet.Spend(player, Shop.ChestPrice, CoinSource.ShopChest, chest.Id, now));
            db.RewardChests.Add(chest);
            chestDto = chest.ToDto();
        }
        else if (request.Item == ShopItem.Theme)
        {
            var themeKey = request.ThemeKey ?? string.Empty;
            if (!Themes.Exists(themeKey))
            {
                throw ApiErrorException.BadRequest("UnknownTheme", $"未知的主題：{themeKey}");
            }

            if ((await OwnedThemesAsync(userId, ct)).Contains(themeKey))
            {
                throw ApiErrorException.BadRequest("ThemeOwned", "已擁有此主題");
            }

            db.CoinEvents.Add(Wallet.Spend(player, Shop.ThemePrice, CoinSource.ShopTheme, null, now));
            db.OwnedThemes.Add(new OwnedTheme { UserId = userId, ThemeKey = themeKey });
        }
        else
        {
            throw ApiErrorException.BadRequest("UnknownItem", "未知的商品");
        }

        await db.SaveChangesAsync(ct);
        var rewards = await rewardApplier.ApplyAsync(context, 0, ct);
        var themes = await OwnedThemesAsync(userId, ct);
        await tx.CommitAsync(ct);
        return new PurchaseResponse(player.Coins, player.ShieldCount, themes, chestDto, rewards);
    }

    /// <summary>
    /// 可使用的主題：預設 azure 加上已購買的，依目錄順序。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>主題鍵清單。</returns>
    private async Task<List<string>> OwnedThemesAsync(Guid userId, CancellationToken ct)
    {
        var purchased = await db.OwnedThemes.Where(t => t.UserId == userId).Select(t => t.ThemeKey).ToListAsync(ct);
        return Themes.All.Where(k => k == Themes.Default || purchased.Contains(k)).ToList();
    }
}
