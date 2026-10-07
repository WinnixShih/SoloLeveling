# 獎勵系統：寶箱、卡片圖鑑、金幣、連勝保險、稱號 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 達成里程碑得到寶箱、開出收藏卡片與金幣；金幣換連勝保險卡、寶箱與主題色；成就解鎖稱號字塊，組成「前綴・後綴」顯示在個人檔案。

**Architecture:** Domain 新增卡片／成就目錄與純規則（`Loot`、`Wallet`、`Rewards.Evaluate`、`QuestRoles`、`GoalCompletion`、`Titles`），保險卡消耗併入 `Settlement.Settle`。Api 新增 `RewardStatsLoader`（批次查統計）與 `RewardApplier`（判定＋持久化，每個會改狀態的請求在第一次 `SaveChangesAsync` 後呼叫一次），回應帶 `rewards` 欄位；另有 `/rewards`、`/cards`、`/shop/purchase`、`/me/title|pinned-card|theme` 端點。前端新增 `hunter.js`（檔案頁、商店、主題區塊），`api()` 統一收集 `rewards`，`announce()` 依序顯示系統訊息。

**Tech Stack:** .NET 10、EF Core 10＋Npgsql、xUnit＋FluentAssertions＋Testcontainers、純 HTML＋JS 前端（UI 改版後的 `ui.js`／`app.js`）

**Spec:** `docs/superpowers/specs/2026-10-01-rewards-cards-titles-design.md`；前端介面約定：`.superpowers/sdd/plan-drafting/frontend-contract.md`（本計畫在 UI 改版計畫完成後執行）

## Global Constraints

- 依賴方向 Api → Infrastructure → Domain；Domain 不得引用框架（`Random` 屬 BCL，可用）。
- 建置開 `TreatWarningsAsErrors`、`EnforceCodeStyleInBuild`；src 專案 public 成員缺 XML 註解就建置失敗；`<param>`／`<returns>`／`<exception>` 要完整，不用 `<inheritdoc />`。
- 控制流一律加大括號；`catch` 只接已知例外；時間來源注入 `TimeProvider`（`clock.GetUtcNow()`），禁止 `DateTime.Now`／`UtcNow`；「今日」只能用 `TodayContext.Today`。
- Player.Xp 的任何變動都要有對應 XpEvent；**Player.Coins 的任何變動都要有對應 CoinEvent，一律經 `Wallet.Change`／`Wallet.Spend`**。
- 服務層改狀態一律「開交易 → `TodayContextLoader.LoadAsync` → 修改 → `SaveChangesAsync` → `RewardApplier.ApplyAsync` → Commit」。
- DB 時間戳 `bigint` Unix 毫秒（全域 converter），API 出口 Unix 秒（`Mappers.ToUnixSeconds`）；只有這兩處換算。前端只在 `hunter.js` 的 `fromUnixSeconds` 轉成 `Date` 顯示。
- DB enum 存字串 `HasConversion<string>().HasMaxLength(16)`；table 與欄位 PascalCase；一支 migration。
- 錯誤格式 `{ error: { code, message } }`：Api 層 `ApiErrorException`，Domain 層 `DomainValidationException(code, message)`。錯誤碼（規格原文）：`NotEnoughCoins`、`ShieldLimitReached`、`ThemeOwned`、`ChestNotFound`（404）、`TitleNotUnlocked`、`CardNotOwned`、`ThemeNotOwned`。
- 數值（規格原文）：開箱金幣 E 20／C 50／A 100／S 300；重複卡金幣 E 30／C 80／A 200／S 500；每日達標 +10；解鎖成就 +50；保險卡 100 金幣、最多持有 3 張；E 級寶箱 200；紫影、翡翠各 500，藍光 `azure` 免費預設。
- 卡片目錄 E 10、C 8、A 5、S 2 共 25 張；`Image` 為 `cards/{id}.webp`；不新增任何圖檔，前端在圖檔 404 時顯示佔位卡。
- 不得用金幣換 EXP、等級或連勝天數；前端不計算 EXP、等級、金幣。
- 註解與文件繁體中文、全形標點；換行 LF；修改既有檔案用 Edit。
- 測試名稱用中文描述行為；Api 整合測試類別加 `[Collection(PostgresCollection.Name)]`，用 `ApiFactory`；`_factory.Clock.Advance(...)` 撥時間，起始 2026-09-28 10:00 UTC。
- 直接在 `main` 上 commit（本專案指示），不 push。Commit header `type: 主旨`（繁中、≤50 字、無句號），body 繁中數字條列最多 3 項，結尾兩行：
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01NxkXCvcUbg2y9cvwZjMor4
  ```

## Rulings（規格未明定、本計畫的決定）

1. **升級寶箱以 `Player.PeakLevel` 判定，不以請求前後差。** 只對超過歷史最高等級的等級發 E 箱、跨階發 C 箱與保險卡，撤銷降級後再升回來不重發（否則反覆勾選／取消可無限刷箱）。規格的 `PlayerSnapshot` 保留，只拿來算回應裡的 `levelsGained`（系統訊息用）；快照在 `TodayContextLoader` 結算**之後**、列鎖內拍（結算不會改 `Level`，結果等同請求前的值，又不會讀到未鎖的舊資料）。migration 把既有玩家的 `PeakLevel` 設為目前 `Level`，不補發歷史升級。
2. **成就（含「最佳連續 7／30 天」的 C／A 箱）以狀態判定**：條件成立且未解鎖就解鎖，唯一主鍵 `(UserId, Key)` 擋重複。排程結算或上線前已達成的條件，會在下一次請求補解鎖一次。
3. **每日達標金幣以 `DailyLog.ClearCoinsGranted` 追蹤**，跟著 `BonusGranted` 發放與收回；收回時最多扣到 0（事件金額等於實際扣除量，0 則不寫事件），餘額永不為負。migration 把既有紀錄的 `ClearCoinsGranted` 設為 `BonusGranted`，不補發。
4. **同一交易內的順序**：`BeginTransaction` → `LoadAsync`（結算＋保險卡消耗＋寫 `RewardEvents`）→ 服務修改＋XpEvent → `SaveChangesAsync` → `RewardApplier.ApplyAsync`（判定目標完成 → 批次查統計 → `Rewards.Evaluate` → 寫寶箱／成就／CoinEvent → 公告保險卡事件 → `SaveChangesAsync`）→ Commit。統計改由 applier 在第一次存檔**之後**查（不是 loader），才看得到本次的進度與新建的目標；規格第 8 節「loader 批次載入統計」據此調整。
5. **`rewards` 欄位的接法**：`TodayResponse`、`MeResponse`、`GoalsResponse`、`QuestDto`、`ProgramDto` 加尾端選填參數 `RewardsDto? Rewards = null`，序列化時 null 省略（清單、`GET /goals`、`/me` 內的 `program` 不帶）。`DELETE /quests/{id}` 與 `DELETE /goals/{id}` 由 204 改為 200 `{ rewards }`（封存可能使今日達標而升級或得金幣）；`PUT /quests/reorder`、`PUT /today/note` 不改狀態，維持 204。`GET /today`、`GET /me` 也套用並回傳 `rewards`；`GET /rewards`、`GET /goals`、`GET /history` 不套用（以免吃掉待公告的保險卡事件）。
6. **保險卡公告**：`RewardEvents` 加 `AnnouncedAt`（nullable）；第一個套用獎勵的請求回報所有未公告事件並寫入時間，因此排程結算消耗的保險卡也會在下一次請求顯示且只顯示一次。
7. **保險卡只在 `Streak > 0` 時消耗**（沒有連勝可保護時不浪費）；晉階送的保險卡受上限 3 截斷。
8. **目標完成**：目標的每個未封存任務，「達標天數（含今天）≥ `(StageCount − 1) × DaysPerStep + 1`」（＝在最後一階至少達標一天）；沒有未封存任務的目標不算完成；`CompletedAt` 寫入後不再清除。
9. **66 天週期完成**：`today − StartDate ≥ LengthDays`，與 `ProgramDto.IsCompleted` 同一判定。
10. **亂數**：DI 註冊 `Random`（`Random.Shared`）注入 `RewardService`；`Loot.Open` 用 `rng.Next(pool.Count)`；測試以 `FixedRandom` 取代。
11. **成就鍵**為 kebab-case，同時是稱號字塊鍵；`PlayerDto.Title` 改為組合後的稱號（沒選字塊時等於階級稱號），新增 `RankTitle`；規格說 `/me` 加的 `coins`、`shieldCount`、`themeKey`、`title`、`pinnedCard` 放在 `me.player` 底下（前端約定的 `UI.statusPanel` 讀 `me.player`）。
12. **分類統計範圍**：連續天數只看未封存的角色任務，查 `[today − 30, today]`；累計分鐘含已封存的運動／閱讀漸進任務（終身累計）；「建立第一個目標」以 `Goals` 列數（含封存）判定，只帶基本任務的 `POST /goals` 不算。
13. **商店檢查順序**：上限／已擁有／主題不存在先於金幣檢查；`themeKey` 不存在回 400 `UnknownTheme`，`azure` 回 `ThemeOwned`；開箱金幣寫兩筆事件（`ChestOpen`、`DuplicateCard`），`RefId` 指向寶箱。
14. **前端**：`api()` 把每個回應的 `rewards` 放進 `state.pendingRewards`；`announceRewards()` 一次取出並依「升級 → 晉階 → 寶箱 → 成就 → 保險卡」顯示。升級訊息改由 `rewards.levelsGained` 觸發，`announce()` 原本比較 `prevMe.player.level` 的升級訊息移除，避免重複與順序錯亂。

## Review Focus

1. **撤銷再完成刷寶箱**：Lv1 → 完成任務升 Lv2 得 E 箱 → 取消完成降回 Lv1 → 再完成升回 Lv2，不得再發 E 箱。→ Task 4 `Evaluate_撤銷後再升回曾到過的等級_不再發箱`、Task 6 `升級後撤銷再完成_不重複發E箱`。
2. **重複開箱與開別人的寶箱**：同一寶箱開第二次、或用別人的寶箱 ID，要回 404 `ChestNotFound`，不得重複發卡與金幣。→ Task 7 `開已開過或別人的寶箱_回404`。
3. **背景排程消耗保險卡**：使用者整天沒開 App、保險卡由 `SettlementScheduler` 消耗，下一次開 App 仍要看到一次「保險已生效」，且第二次請求不再重複。→ Task 6 `排程結算消耗保險卡_下次請求公告一次`。
4. **達標金幣花掉後撤銷達標**：餘額已低於 10 時取消達標，餘額不得變負，事件金額等於實際扣除量。→ Task 4 `Evaluate_撤銷達標但餘額不足_只扣到0`。
5. **沒有連勝時漏一天**：Streak 為 0 的玩家漏一天，不得白白消耗保險卡。→ Task 3 `Settle_Streak為0時未達標_不消耗保險卡`。

## File Structure

| 檔案 | 責任 | Task |
| --- | --- | --- |
| `src/SoloLeveling.Domain/Enums.cs` | 新 enum：`Rarity`、`ChestSource`、`CoinSource`、`RewardEventKind`、`TitleSlot`、`QuestRole`、`ShopItem` | 1 |
| `src/SoloLeveling.Domain/Cards.cs` | `CardDefinition`、`Cards.All`（25 張） | 1 |
| `src/SoloLeveling.Domain/Achievements.cs` | `AchievementStats`、`AchievementDefinition`、`Achievements.All`（12 個） | 1 |
| `src/SoloLeveling.Domain/Themes.cs`、`Shop.cs` | 主題鍵、商店價格與保險卡上限 | 1 |
| `src/SoloLeveling.Domain/Rules/Titles.cs` | 稱號組合 | 1 |
| `src/SoloLeveling.Domain/Entities/*` | 新實體 6 個；`Player`、`Goal`、`Program`、`DailyLog` 加欄位 | 2 |
| `src/SoloLeveling.Domain/Rules/Loot.cs`、`Wallet.cs` | 開箱抽卡；金幣異動與事件 | 2 |
| `src/SoloLeveling.Domain/Rules/Settlement.cs` | 保險卡逐日消耗、`SettlementResult.ShieldsUsed` | 3 |
| `src/SoloLeveling.Domain/Rules/QuestRoles.cs`、`GoalCompletion.cs`、`Rewards.cs` | 任務角色與分類連續天數、目標完成判定、獎勵判定 | 4 |
| `src/SoloLeveling.Infrastructure/AppDbContext.cs`、`Migrations/*_AddRewards.cs`、`SettlementService.cs` | 對應、migration、寫 `RewardEvents` | 5 |
| `src/SoloLeveling.Api/Services/RewardStatsLoader.cs`、`RewardApplier.cs` | 批次統計；判定＋持久化 | 6 |
| `src/SoloLeveling.Api/Contracts/RewardDtos.cs` | 獎勵相關 DTO | 6、7 |
| `src/SoloLeveling.Api/Services/RewardService.cs`、`Controllers/RewardsController.cs` | `/rewards`、開箱、`/cards`、商店 | 7 |
| `src/SoloLeveling.Api/wwwroot/hunter.js` 等 | 檔案頁、商店、主題、系統訊息 | 8 |
| `docs/SPEC.md`、`docs/ARCHITECTURE.md`、`README.md`、`CLAUDE.md` | 文件 | 9 |

---

### Task 1: Domain 目錄：enum、卡片、成就、主題、商店常數、稱號組合

**Files:**
- Modify: `src/SoloLeveling.Domain/Enums.cs`（檔尾新增 7 個 enum）
- Create: `src/SoloLeveling.Domain/Cards.cs`
- Create: `src/SoloLeveling.Domain/Achievements.cs`
- Create: `src/SoloLeveling.Domain/Themes.cs`
- Create: `src/SoloLeveling.Domain/Shop.cs`
- Create: `src/SoloLeveling.Domain/Rules/Titles.cs`
- Test: `tests/SoloLeveling.Domain.Tests/RewardCatalogTests.cs`

**Interfaces:**
- Consumes: `Leveling.RankOf(int level) → PlayerRank(Rank, Title)`。
- Produces:
  - `enum Rarity { E = 1, C = 2, A = 3, S = 4 }`
  - `enum ChestSource { LevelUp = 1, RankUp = 2, Streak7 = 3, Streak30 = 4, GoalCompleted = 5, ProgramCompleted = 6, Purchase = 7 }`
  - `enum CoinSource { DailyClear = 1, DailyClearUndo = 2, ChestOpen = 3, DuplicateCard = 4, Achievement = 5, ShopShield = 6, ShopChest = 7, ShopTheme = 8 }`
  - `enum RewardEventKind { ShieldUsed = 1 }`、`enum TitleSlot { Prefix = 1, Suffix = 2 }`
  - `enum QuestRole { Bedtime = 1, WakeUp = 2, Exercise = 3, Reading = 4, ScreenTime = 5 }`、`enum ShopItem { Shield = 1, EChest = 2, Theme = 3 }`
  - `record CardDefinition(string Id, string Name, Rarity Rarity, string Flavor)`，屬性 `string Image`
  - `Cards.All: IReadOnlyList<CardDefinition>`、`Cards.Find(string id) → CardDefinition?`、`Cards.OfRarity(Rarity) → IReadOnlyList<CardDefinition>`
  - `record AchievementStats(int GoalCount, int BestStreak, int TotalCompleted, IReadOnlyDictionary<QuestRole, int> RoleStreaks, IReadOnlyDictionary<QuestRole, int> RoleMinutes, int OwnedCardKinds, int CompletedPrograms, int PeakLevel)`，`AchievementStats.Empty`、`StreakOf(QuestRole) → int`、`MinutesOf(QuestRole) → int`
  - `record AchievementDefinition(string Key, string Name, string Condition, string TitleText, TitleSlot Slot, int Target, Func<AchievementStats, int> Measure)`，`IsMet(AchievementStats) → bool`、`ProgressOf(AchievementStats) → int`
  - `Achievements.All`、`Achievements.Find(string key) → AchievementDefinition?`、常數 `Streak7Key = "streak-7"`、`Streak30Key = "streak-30"`、`SRankLevel = 45`
  - `Themes.Default = "azure"`、`Themes.All`、`Themes.Exists(string) → bool`
  - `Shop.ShieldPrice = 100`、`Shop.ChestPrice = 200`、`Shop.ThemePrice = 500`、`Shop.MaxShields = 3`
  - `Titles.Separator = "・"`、`Titles.Compose(string? prefixKey, string? suffixKey, int level) → string`

- [ ] **Step 1: 寫失敗的測試**

建立 `tests/SoloLeveling.Domain.Tests/RewardCatalogTests.cs`：

```csharp
using System.Text.RegularExpressions;
using FluentAssertions;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class RewardCatalogTests
{
    [Fact]
    public void Cards_共25張_E10C8A5S2()
    {
        Cards.All.Should().HaveCount(25);
        Cards.OfRarity(Rarity.E).Should().HaveCount(10);
        Cards.OfRarity(Rarity.C).Should().HaveCount(8);
        Cards.OfRarity(Rarity.A).Should().HaveCount(5);
        Cards.OfRarity(Rarity.S).Should().HaveCount(2);
    }

    [Fact]
    public void Cards_Id唯一且為kebabcase_圖片路徑依Id()
    {
        Cards.All.Select(c => c.Id).Should().OnlyHaveUniqueItems();
        Cards.All.Should().OnlyContain(c => Regex.IsMatch(c.Id, "^[a-z0-9]+(-[a-z0-9]+)*$"));
        Cards.All.Should().OnlyContain(c => c.Image == $"cards/{c.Id}.webp");
        Cards.All.Should().OnlyContain(c => c.Name.Length > 0 && c.Flavor.Length > 0);
    }

    [Fact]
    public void Cards_Find_找得到既有卡_找不到回null()
    {
        Cards.Find("rusty-dagger")!.Rarity.Should().Be(Rarity.E);
        Cards.Find("no-such-card").Should().BeNull();
        Cards.OfRarity(Rarity.E)[0].Id.Should().Be("rusty-dagger");
    }

    [Fact]
    public void Achievements_共12個且鍵唯一()
    {
        Achievements.All.Should().HaveCount(12);
        Achievements.All.Select(a => a.Key).Should().OnlyHaveUniqueItems();
        Achievements.Find("no-such").Should().BeNull();
    }

    [Theory]
    [InlineData("first-goal", "初次覺醒", "覺醒的", TitleSlot.Prefix)]
    [InlineData("streak-7", "不屈", "不屈的", TitleSlot.Prefix)]
    [InlineData("streak-30", "恆心", "恆心的", TitleSlot.Prefix)]
    [InlineData("quests-100", "百戰", "百戰獵人", TitleSlot.Suffix)]
    [InlineData("wake-30", "晨曦", "晨曦騎士", TitleSlot.Suffix)]
    [InlineData("bed-30", "靜夜", "靜夜的", TitleSlot.Prefix)]
    [InlineData("screen-30", "手機克星", "手機克星", TitleSlot.Suffix)]
    [InlineData("exercise-1000", "百里", "百里行者", TitleSlot.Suffix)]
    [InlineData("reading-1000", "藏書", "藏書者", TitleSlot.Suffix)]
    [InlineData("program-complete", "破繭", "破繭的", TitleSlot.Prefix)]
    [InlineData("collector-10", "收藏家", "收藏家", TitleSlot.Suffix)]
    [InlineData("rank-s", "傳說", "傳說的", TitleSlot.Prefix)]
    public void Achievements_第一批的名稱字塊與槽位(string key, string name, string text, TitleSlot slot)
    {
        var achievement = Achievements.Find(key)!;
        achievement.Name.Should().Be(name);
        achievement.TitleText.Should().Be(text);
        achievement.Slot.Should().Be(slot);
    }

    [Theory]
    [InlineData("first-goal", 1)]
    [InlineData("streak-7", 7)]
    [InlineData("streak-30", 30)]
    [InlineData("quests-100", 100)]
    [InlineData("wake-30", 30)]
    [InlineData("bed-30", 30)]
    [InlineData("screen-30", 30)]
    [InlineData("exercise-1000", 1000)]
    [InlineData("reading-1000", 1000)]
    [InlineData("program-complete", 1)]
    [InlineData("collector-10", 10)]
    [InlineData("rank-s", 45)]
    public void Achievements_達到門檻才解鎖(string key, int target)
    {
        var achievement = Achievements.Find(key)!;
        achievement.Target.Should().Be(target);
        achievement.IsMet(StatsFor(key, target)).Should().BeTrue();
        achievement.IsMet(StatsFor(key, target - 1)).Should().BeFalse();
    }

    [Fact]
    public void Achievements_分類連續只看自己的角色()
    {
        Achievements.Find("bed-30")!.IsMet(StatsFor("wake-30", 30)).Should().BeFalse();
    }

    [Fact]
    public void Achievements_進度不超過門檻()
    {
        Achievements.Find("quests-100")!.ProgressOf(StatsFor("quests-100", 250)).Should().Be(100);
        Achievements.Find("quests-100")!.ProgressOf(StatsFor("quests-100", 37)).Should().Be(37);
    }

    [Fact]
    public void Achievements_S階門檻與RankOf一致()
    {
        Leveling.RankOf(Achievements.SRankLevel).Rank.Should().Be("S");
        Leveling.RankOf(Achievements.SRankLevel - 1).Rank.Should().Be("A");
    }

    [Theory]
    [InlineData("bed-30", "quests-100", 1, "靜夜的・百戰獵人")]
    [InlineData("streak-7", null, 1, "不屈的")]
    [InlineData(null, "collector-10", 1, "收藏家")]
    [InlineData(null, null, 1, "新手")]
    [InlineData(null, null, 50, "傳說")]
    public void Titles_Compose_有選字塊就組合_都沒選沿用階級稱號(string? prefix, string? suffix, int level, string expected)
    {
        Titles.Compose(prefix, suffix, level).Should().Be(expected);
    }

    [Fact]
    public void Themes與Shop_常數符合規格()
    {
        Themes.Default.Should().Be("azure");
        Themes.All.Should().Equal("azure", "violet", "jade");
        Themes.Exists("violet").Should().BeTrue();
        Themes.Exists("neon").Should().BeFalse();
        Shop.ShieldPrice.Should().Be(100);
        Shop.ChestPrice.Should().Be(200);
        Shop.ThemePrice.Should().Be(500);
        Shop.MaxShields.Should().Be(3);
    }

    private static AchievementStats StatsFor(string key, int value)
    {
        var empty = AchievementStats.Empty;
        return key switch
        {
            "first-goal" => empty with { GoalCount = value },
            "streak-7" or "streak-30" => empty with { BestStreak = value },
            "quests-100" => empty with { TotalCompleted = value },
            "wake-30" => empty with { RoleStreaks = new Dictionary<QuestRole, int> { [QuestRole.WakeUp] = value } },
            "bed-30" => empty with { RoleStreaks = new Dictionary<QuestRole, int> { [QuestRole.Bedtime] = value } },
            "screen-30" => empty with { RoleStreaks = new Dictionary<QuestRole, int> { [QuestRole.ScreenTime] = value } },
            "exercise-1000" => empty with { RoleMinutes = new Dictionary<QuestRole, int> { [QuestRole.Exercise] = value } },
            "reading-1000" => empty with { RoleMinutes = new Dictionary<QuestRole, int> { [QuestRole.Reading] = value } },
            "program-complete" => empty with { CompletedPrograms = value },
            "collector-10" => empty with { OwnedCardKinds = value },
            "rank-s" => empty with { PeakLevel = value },
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "測試未涵蓋的成就"),
        };
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/SoloLeveling.Domain.Tests --filter "FullyQualifiedName~RewardCatalogTests"`
Expected: 建置失敗，CS0103／CS0246 指出 `Cards`、`Achievements`、`Rarity`、`TitleSlot`、`Titles`、`Themes`、`Shop` 不存在。

- [ ] **Step 3: 新增 enum**

用 Edit 在 `src/SoloLeveling.Domain/Enums.cs` 檔尾（`ProgressionValueKind` 的結尾 `}` 之後）加入：

```csharp

/// <summary>
/// 卡片稀有度，也是寶箱等級；兩者一對一（E 級寶箱只開出 E 級卡）。
/// </summary>
public enum Rarity
{
    /// <summary>E 級。</summary>
    E = 1,

    /// <summary>C 級。</summary>
    C = 2,

    /// <summary>A 級。</summary>
    A = 3,

    /// <summary>S 級。</summary>
    S = 4,
}

/// <summary>
/// 寶箱的取得來源。
/// </summary>
public enum ChestSource
{
    /// <summary>升 1 級（E）。</summary>
    LevelUp = 1,

    /// <summary>階級晉升（C）。</summary>
    RankUp = 2,

    /// <summary>最佳連續首次達 7 天（C）。</summary>
    Streak7 = 3,

    /// <summary>最佳連續首次達 30 天（A）。</summary>
    Streak30 = 4,

    /// <summary>目標完成（A）。</summary>
    GoalCompleted = 5,

    /// <summary>66 天週期完成（S）。</summary>
    ProgramCompleted = 6,

    /// <summary>商店購買（E）。</summary>
    Purchase = 7,
}

/// <summary>
/// 金幣流水的來源。
/// </summary>
public enum CoinSource
{
    /// <summary>今日首次達標。</summary>
    DailyClear = 1,

    /// <summary>收回今日達標金幣。</summary>
    DailyClearUndo = 2,

    /// <summary>開箱固定金幣。</summary>
    ChestOpen = 3,

    /// <summary>重複卡轉換。</summary>
    DuplicateCard = 4,

    /// <summary>解鎖成就。</summary>
    Achievement = 5,

    /// <summary>購買連勝保險卡。</summary>
    ShopShield = 6,

    /// <summary>購買 E 級寶箱。</summary>
    ShopChest = 7,

    /// <summary>購買主題。</summary>
    ShopTheme = 8,
}

/// <summary>
/// 獎勵相關事件的種類。
/// </summary>
public enum RewardEventKind
{
    /// <summary>結算時自動消耗 1 張連勝保險卡。</summary>
    ShieldUsed = 1,
}

/// <summary>
/// 稱號字塊的位置。
/// </summary>
public enum TitleSlot
{
    /// <summary>前綴（例：靜夜的）。</summary>
    Prefix = 1,

    /// <summary>後綴（例：百戰獵人）。</summary>
    Suffix = 2,
}

/// <summary>
/// 由漸進任務推導出的角色，供分類成就使用；一般任務沒有角色。
/// </summary>
public enum QuestRole
{
    /// <summary>就寢（作息、<see cref="ProgressionValueKind.TimeOfDay"/>）。</summary>
    Bedtime = 1,

    /// <summary>起床（作息、<see cref="ProgressionValueKind.TimeOfDayEvening"/>）。</summary>
    WakeUp = 2,

    /// <summary>運動。</summary>
    Exercise = 3,

    /// <summary>閱讀。</summary>
    Reading = 4,

    /// <summary>螢幕時間。</summary>
    ScreenTime = 5,
}

/// <summary>
/// 商店商品。
/// </summary>
public enum ShopItem
{
    /// <summary>連勝保險卡。</summary>
    Shield = 1,

    /// <summary>E 級寶箱。</summary>
    EChest = 2,

    /// <summary>主題色（需指定 themeKey）。</summary>
    Theme = 3,
}
```

- [ ] **Step 4: 卡片目錄**

建立 `src/SoloLeveling.Domain/Cards.cs`：

```csharp
namespace SoloLeveling.Domain;

/// <summary>
/// 卡片目錄中的一張卡。
/// </summary>
/// <param name="Id">kebab-case 識別碼，也是插畫檔名。</param>
/// <param name="Name">名稱。</param>
/// <param name="Rarity">稀有度。</param>
/// <param name="Flavor">一兩句風味文字。</param>
public sealed record CardDefinition(string Id, string Name, Rarity Rarity, string Flavor)
{
    /// <summary>插畫相對路徑（<c>cards/{Id}.webp</c>）；檔案不存在時前端顯示佔位卡。</summary>
    public string Image => $"cards/{Id}.webp";
}

/// <summary>
/// 全部卡片。順序即圖鑑顯示順序；同稀有度內的順序也是抽卡時的索引順序（見 <see cref="Rules.Loot"/>）。
/// </summary>
public static class Cards
{
    /// <summary>全部卡片：E 10、C 8、A 5、S 2。</summary>
    public static IReadOnlyList<CardDefinition> All { get; } =
    [
        new("rusty-dagger", "生鏽的短劍", Rarity.E, "每個獵人的第一把武器。刀刃鈍了，握柄還記得你的手。"),
        new("healing-potion", "低階回復藥水", Rarity.E, "苦得要命。喝下去的那一刻，你決定明天還要再來。"),
        new("dungeon-key", "地下城鑰匙", Rarity.E, "E 級傳送門的鑰匙，冰冷又平凡，卻能打開第一扇門。"),
        new("leather-boots", "舊皮靴", Rarity.E, "鞋底磨平了，代表你走過的路比昨天多。"),
        new("system-window", "系統視窗", Rarity.E, "「每日任務已更新。」它從不催促，只是一直都在。"),
        new("goblin-mask", "哥布林面具", Rarity.E, "第一次討伐的戰利品，很醜，但值得留著。"),
        new("dungeon-torch", "地下城火把", Rarity.E, "火光只照得亮前方三步，剛好夠走下一步。"),
        new("mana-shard", "魔力結晶碎片", Rarity.E, "微弱的藍光，像清晨還沒完全醒來的天空。"),
        new("training-weights", "訓練負重", Rarity.E, "系統說：伏地挺身一百下。你說：好。"),
        new("hunter-license", "獵人證", Rarity.E, "照片拍得很差，但上面的名字是你的。"),
        new("knight-shield", "騎士之盾", Rarity.C, "盾面滿是刮痕，每一道都是沒有退後的證明。"),
        new("blue-gate", "藍色傳送門", Rarity.C, "門後的空氣比外面冷，心跳比平常快。"),
        new("iron-golem", "鐵之魔像", Rarity.C, "它不會累，也不會停。你開始懂它了。"),
        new("shadow-step", "影步", Rarity.C, "腳步聲消失的瞬間，你已經在下一個位置。"),
        new("focus-elixir", "專注藥劑", Rarity.C, "喝下後世界安靜了，只剩眼前這一件事。"),
        new("dungeon-map", "地下城地圖", Rarity.C, "地圖會自己補上你走過的路。"),
        new("shadow-wolves", "暗影狼群", Rarity.C, "牠們在黑暗裡跟著你，不是追殺，是同行。"),
        new("daily-chest", "每日寶箱", Rarity.C, "每天打開一次，裡面放的是昨天的你留下的東西。"),
        new("demon-castle", "惡魔城", Rarity.A, "一百層的高塔，第一層的門已經為你打開。"),
        new("shadow-knight", "影之騎士長", Rarity.A, "跪下的那一刻，它稱你為「主上」。"),
        new("red-gate", "紅色傳送門", Rarity.A, "進得去，出不來，除非你比昨天更強。"),
        new("double-dungeon", "雙重地下城", Rarity.A, "神像在微笑。那是一切的開始。"),
        new("twin-daggers", "君王的雙刃", Rarity.A, "輕得像沒有重量，鋒利得像決心。"),
        new("shadow-monarch", "影之君主", Rarity.S, "「起來吧。」黑暗回應了你的聲音。"),
        new("arise", "起來吧", Rarity.S, "所有被你擊倒過的懶惰與藉口，如今都站在你身後。"),
    ];

    private static readonly Dictionary<string, CardDefinition> Index = All.ToDictionary(c => c.Id);

    /// <summary>
    /// 依 ID 取卡片。
    /// </summary>
    /// <param name="id">卡片 ID。</param>
    /// <returns>卡片；不存在回 null。</returns>
    public static CardDefinition? Find(string id)
    {
        return Index.GetValueOrDefault(id);
    }

    /// <summary>
    /// 取某稀有度的全部卡片，依目錄順序。
    /// </summary>
    /// <param name="rarity">稀有度。</param>
    /// <returns>卡片清單。</returns>
    public static IReadOnlyList<CardDefinition> OfRarity(Rarity rarity)
    {
        return All.Where(c => c.Rarity == rarity).ToList();
    }
}
```

- [ ] **Step 5: 成就目錄**

建立 `src/SoloLeveling.Domain/Achievements.cs`：

```csharp
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
```

- [ ] **Step 6: 主題與商店常數**

建立 `src/SoloLeveling.Domain/Themes.cs`：

```csharp
namespace SoloLeveling.Domain;

/// <summary>
/// 介面主題鍵；對應前端 <c>&lt;html data-accent&gt;</c>。
/// </summary>
public static class Themes
{
    /// <summary>預設主題（藍光），免費、不需購買。</summary>
    public const string Default = "azure";

    /// <summary>全部主題：藍光、紫影、翡翠。</summary>
    public static IReadOnlyList<string> All { get; } = [Default, "violet", "jade"];

    /// <summary>
    /// 主題鍵是否存在。
    /// </summary>
    /// <param name="key">主題鍵。</param>
    /// <returns>是否為已知主題。</returns>
    public static bool Exists(string key)
    {
        return All.Contains(key);
    }
}
```

建立 `src/SoloLeveling.Domain/Shop.cs`：

```csharp
namespace SoloLeveling.Domain;

/// <summary>
/// 商店價格與持有上限。
/// </summary>
public static class Shop
{
    /// <summary>連勝保險卡價格。</summary>
    public const int ShieldPrice = 100;

    /// <summary>E 級寶箱價格。</summary>
    public const int ChestPrice = 200;

    /// <summary>主題（紫影、翡翠）價格。</summary>
    public const int ThemePrice = 500;

    /// <summary>連勝保險卡最多持有張數；購買與晉階贈送都受此限制。</summary>
    public const int MaxShields = 3;
}
```

- [ ] **Step 7: 稱號組合**

建立 `src/SoloLeveling.Domain/Rules/Titles.cs`：

```csharp
namespace SoloLeveling.Domain.Rules;

/// <summary>
/// 稱號組合規則：有選字塊 → 「前綴・後綴」（只選一邊就只顯示該邊）；都沒選 → 階級稱號。
/// </summary>
public static class Titles
{
    /// <summary>前綴與後綴之間的分隔字。</summary>
    public const string Separator = "・";

    /// <summary>
    /// 組出顯示用稱號。
    /// </summary>
    /// <param name="prefixKey">前綴字塊鍵；null 表示不選。</param>
    /// <param name="suffixKey">後綴字塊鍵；null 表示不選。</param>
    /// <param name="level">目前等級，都沒選時取階級稱號。</param>
    /// <returns>顯示用稱號。</returns>
    public static string Compose(string? prefixKey, string? suffixKey, int level)
    {
        var parts = new[] { prefixKey, suffixKey }
            .Where(k => k is not null)
            .Select(k => Achievements.Find(k!)?.TitleText)
            .OfType<string>()
            .ToList();
        return parts.Count == 0 ? Leveling.RankOf(level).Title : string.Join(Separator, parts);
    }
}
```

- [ ] **Step 8: 跑測試確認通過**

Run: `dotnet test tests/SoloLeveling.Domain.Tests --filter "FullyQualifiedName~RewardCatalogTests"`
Expected: 全部 PASS（37 個：8 個 Fact＋12＋12＋5 個 Theory 案例）。

Run: `dotnet build && dotnet format --verify-no-changes`
Expected: 0 警告 0 錯誤、無格式差異。

- [ ] **Step 9: Commit**

```bash
git add src/SoloLeveling.Domain/Enums.cs src/SoloLeveling.Domain/Cards.cs src/SoloLeveling.Domain/Achievements.cs src/SoloLeveling.Domain/Themes.cs src/SoloLeveling.Domain/Shop.cs src/SoloLeveling.Domain/Rules/Titles.cs tests/SoloLeveling.Domain.Tests/RewardCatalogTests.cs
git commit -F - <<'EOF'
feat: 新增卡片與成就目錄、獎勵 enum 與稱號組合

1. 卡片 25 張與成就 12 個定義在 Domain 常數清單，插畫可晚於目錄存在
2. 成就以統計快照判定門檻，稱號字塊鍵沿用成就鍵

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NxkXCvcUbg2y9cvwZjMor4
EOF
```

---

### Task 2: 實體欄位與新實體、開箱抽卡、金幣異動

**Files:**
- Modify: `src/SoloLeveling.Domain/Entities/Player.cs`（`TotalCompleted` 之後加 7 個欄位）
- Modify: `src/SoloLeveling.Domain/Entities/Goal.cs`（`ArchivedAt` 之後加 `CompletedAt`）
- Modify: `src/SoloLeveling.Domain/Entities/Program.cs`（`IsActive` 之後加 `CompletedAt`）
- Modify: `src/SoloLeveling.Domain/Entities/DailyLog.cs`（`BonusGranted` 之後加 `ClearCoinsGranted`）
- Create: `src/SoloLeveling.Domain/Entities/RewardChest.cs`、`OwnedCard.cs`、`Achievement.cs`、`OwnedTheme.cs`、`CoinEvent.cs`、`RewardEvent.cs`
- Create: `src/SoloLeveling.Domain/Rules/Loot.cs`、`src/SoloLeveling.Domain/Rules/Wallet.cs`
- Test: `tests/SoloLeveling.Domain.Tests/LootTests.cs`、`tests/SoloLeveling.Domain.Tests/WalletTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `Rarity`、`ChestSource`、`CoinSource`、`RewardEventKind`、`Cards.OfRarity`、`Themes.Default`、`DomainValidationException`。
- Produces:
  - `Player.PeakLevel: int = 1`、`Coins: int`、`ShieldCount: int`、`ThemeKey: string = "azure"`、`TitlePrefixKey: string?`、`TitleSuffixKey: string?`、`PinnedCardId: string?`
  - `Goal.CompletedAt: DateTimeOffset?`、`Program.CompletedAt: DateTimeOffset?`、`DailyLog.ClearCoinsGranted: bool`
  - `RewardChest { Guid Id; Guid UserId; Rarity Rarity; ChestSource Source; DateTimeOffset CreatedAt; DateTimeOffset? OpenedAt; string? DroppedCardId; int Coins }`
  - `OwnedCard { Guid UserId; string CardId; int Count; DateTimeOffset FirstAcquiredAt }`
  - `Achievement { Guid UserId; string Key; DateTimeOffset UnlockedAt }`
  - `OwnedTheme { Guid UserId; string ThemeKey }`
  - `CoinEvent { Guid Id; long Seq; Guid UserId; int Amount; CoinSource Source; Guid? RefId; DateTimeOffset OccurredAt }`
  - `RewardEvent { Guid Id; Guid UserId; RewardEventKind Kind; DateOnly Date; DateTimeOffset OccurredAt; DateTimeOffset? AnnouncedAt }`
  - `record LootResult(string CardId, bool IsDuplicate, int BaseCoins, int DuplicateCoins)`，`int Coins`
  - `Loot.OpenCoinsOf(Rarity) → int`、`Loot.DuplicateCoinsOf(Rarity) → int`、`Loot.Open(Rarity rarity, IReadOnlyDictionary<string, int> ownedCounts, Random rng) → LootResult`
  - `Wallet.Change(Player player, int amount, CoinSource source, Guid? refId, DateTimeOffset now) → CoinEvent`、`Wallet.Spend(Player player, int price, CoinSource source, Guid? refId, DateTimeOffset now) → CoinEvent`

- [ ] **Step 1: 寫失敗的測試**

建立 `tests/SoloLeveling.Domain.Tests/LootTests.cs`：

```csharp
using FluentAssertions;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class LootTests
{
    /// <summary>固定回傳指定索引（超過上限取最後一個），並記下最後一次的上限。</summary>
    private sealed class FixedRandom(int value) : Random
    {
        public int? LastMaxValue { get; private set; }

        public override int Next(int maxValue)
        {
            LastMaxValue = maxValue;
            return Math.Min(value, maxValue - 1);
        }
    }

    [Fact]
    public void Open_未擁有_成為新卡且只得開箱金幣()
    {
        var result = Loot.Open(Rarity.E, new Dictionary<string, int>(), new FixedRandom(0));

        result.Should().Be(new LootResult("rusty-dagger", false, 20, 0));
        result.Coins.Should().Be(20);
    }

    [Fact]
    public void Open_已擁有_重複卡另得重複金幣()
    {
        var result = Loot.Open(Rarity.E, new Dictionary<string, int> { ["rusty-dagger"] = 1 }, new FixedRandom(0));

        result.Should().Be(new LootResult("rusty-dagger", true, 20, 30));
        result.Coins.Should().Be(50);
    }

    [Theory]
    [InlineData(Rarity.E, 20, 30)]
    [InlineData(Rarity.C, 50, 80)]
    [InlineData(Rarity.A, 100, 200)]
    [InlineData(Rarity.S, 300, 500)]
    public void Open_各等級的開箱金幣與重複金幣(Rarity rarity, int baseCoins, int duplicateCoins)
    {
        var cardId = Cards.OfRarity(rarity)[1].Id;

        var result = Loot.Open(rarity, new Dictionary<string, int> { [cardId] = 2 }, new FixedRandom(1));

        result.Should().Be(new LootResult(cardId, true, baseCoins, duplicateCoins));
    }

    [Theory]
    [InlineData(Rarity.E, 10)]
    [InlineData(Rarity.C, 8)]
    [InlineData(Rarity.A, 5)]
    [InlineData(Rarity.S, 2)]
    public void Open_從該等級全部卡片均勻抽_含已擁有(Rarity rarity, int poolSize)
    {
        var rng = new FixedRandom(99);
        var owned = Cards.OfRarity(rarity).ToDictionary(c => c.Id, _ => 1);

        var result = Loot.Open(rarity, owned, rng);

        rng.LastMaxValue.Should().Be(poolSize);
        Cards.Find(result.CardId)!.Rarity.Should().Be(rarity);
        result.IsDuplicate.Should().BeTrue();
    }
}
```

建立 `tests/SoloLeveling.Domain.Tests/WalletTests.cs`：

```csharp
using FluentAssertions;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class WalletTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static Player NewPlayer(int coins)
    {
        return new Player { UserId = Guid.NewGuid(), Coins = coins };
    }

    [Fact]
    public void Change_加金幣_餘額增加並回傳對應事件()
    {
        var player = NewPlayer(5);
        var refId = Guid.NewGuid();

        var coinEvent = Wallet.Change(player, 20, CoinSource.ChestOpen, refId, Now);

        player.Coins.Should().Be(25);
        coinEvent.Id.Should().NotBe(Guid.Empty);
        coinEvent.UserId.Should().Be(player.UserId);
        coinEvent.Amount.Should().Be(20);
        coinEvent.Source.Should().Be(CoinSource.ChestOpen);
        coinEvent.RefId.Should().Be(refId);
        coinEvent.OccurredAt.Should().Be(Now);
    }

    [Fact]
    public void Change_扣到負數_丟InvalidOperationException且餘額不變()
    {
        var player = NewPlayer(5);

        var act = () => Wallet.Change(player, -6, CoinSource.DailyClearUndo, null, Now);

        act.Should().Throw<InvalidOperationException>();
        player.Coins.Should().Be(5);
    }

    [Fact]
    public void Spend_餘額不足_丟NotEnoughCoins且餘額不變()
    {
        var player = NewPlayer(99);

        var act = () => Wallet.Spend(player, Shop.ShieldPrice, CoinSource.ShopShield, null, Now);

        act.Should().Throw<DomainValidationException>().Which.Code.Should().Be("NotEnoughCoins");
        player.Coins.Should().Be(99);
    }

    [Fact]
    public void Spend_餘額足夠_扣款並回傳負數事件()
    {
        var player = NewPlayer(250);

        var coinEvent = Wallet.Spend(player, Shop.ChestPrice, CoinSource.ShopChest, null, Now);

        player.Coins.Should().Be(50);
        coinEvent.Amount.Should().Be(-200);
        coinEvent.Source.Should().Be(CoinSource.ShopChest);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/SoloLeveling.Domain.Tests --filter "FullyQualifiedName~LootTests|FullyQualifiedName~WalletTests"`
Expected: 建置失敗，`Loot`、`LootResult`、`Wallet`、`Player.Coins`、`CoinEvent` 不存在。

- [ ] **Step 3: 既有實體加欄位**

用 Edit 在 `src/SoloLeveling.Domain/Entities/Player.cs` 的

```csharp
    /// <summary>累計完成任務次數；撤銷完成時會減回。</summary>
    public int TotalCompleted { get; set; }
```

之後加入：

```csharp

    /// <summary>曾到達的最高等級；升級寶箱與晉階獎勵只對超過此值的等級發放，撤銷降級後再升回來不重複發。</summary>
    public int PeakLevel { get; set; } = 1;

    /// <summary>金幣餘額，永遠 &gt;= 0；每次變動都必須對應一筆 <see cref="CoinEvent"/>（經 <c>Wallet</c>）。</summary>
    public int Coins { get; set; }

    /// <summary>連勝保險卡張數（0 到 <c>Shop.MaxShields</c>）；結算遇到未達標日且連勝進行中時自動消耗。</summary>
    public int ShieldCount { get; set; }

    /// <summary>目前主題鍵（azure／violet／jade）。</summary>
    public string ThemeKey { get; set; } = Themes.Default;

    /// <summary>稱號前綴字塊（成就鍵）；null 表示不選。</summary>
    public string? TitlePrefixKey { get; set; }

    /// <summary>稱號後綴字塊（成就鍵）；null 表示不選。</summary>
    public string? TitleSuffixKey { get; set; }

    /// <summary>釘選展示的卡片 ID；null 表示不釘選。</summary>
    public string? PinnedCardId { get; set; }
```

用 Edit 在 `src/SoloLeveling.Domain/Entities/Goal.cs` 的

```csharp
    /// <summary>封存時間（UTC）。</summary>
    public DateTimeOffset? ArchivedAt { get; set; }
```

之後加入：

```csharp

    /// <summary>完成時間（UTC）：所有未封存任務都在最後一階達標過時寫入，之後不再清除；用來保證 A 級寶箱只發一次。</summary>
    public DateTimeOffset? CompletedAt { get; set; }
```

用 Edit 在 `src/SoloLeveling.Domain/Entities/Program.cs` 的

```csharp
    /// <summary>是否為目前進行中的週期。</summary>
    public bool IsActive { get; set; } = true;
```

之後加入：

```csharp

    /// <summary>完成時間（UTC）：開新週期時舊週期已滿 <see cref="LengthDays"/> 天才寫入；未完成為 null。</summary>
    public DateTimeOffset? CompletedAt { get; set; }
```

用 Edit 在 `src/SoloLeveling.Domain/Entities/DailyLog.cs` 的

```csharp
    /// <summary>是否已發放當日達標獎勵（+30 EXP）。</summary>
    public bool BonusGranted { get; set; }
```

之後加入：

```csharp

    /// <summary>是否已發放當日達標金幣（+10）；跟著 <see cref="BonusGranted"/> 發放與收回。</summary>
    public bool ClearCoinsGranted { get; set; }
```

- [ ] **Step 4: 新實體**

建立 `src/SoloLeveling.Domain/Entities/RewardChest.cs`：

```csharp
namespace SoloLeveling.Domain.Entities;

/// <summary>
/// 寶箱；建立時未開啟，使用者手動開啟後寫入掉落卡片與金幣。
/// </summary>
public class RewardChest
{
    /// <summary>主鍵。</summary>
    public Guid Id { get; set; }

    /// <summary>所屬使用者。</summary>
    public Guid UserId { get; set; }

    /// <summary>寶箱等級，決定抽卡池與金幣。</summary>
    public Rarity Rarity { get; set; }

    /// <summary>取得來源。</summary>
    public ChestSource Source { get; set; }

    /// <summary>取得時間（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>開啟時間（UTC）；未開啟為 null。</summary>
    public DateTimeOffset? OpenedAt { get; set; }

    /// <summary>掉落的卡片 ID；未開啟為 null。</summary>
    public string? DroppedCardId { get; set; }

    /// <summary>開啟時得到的金幣（開箱＋重複卡）；未開啟為 0。</summary>
    public int Coins { get; set; }
}
```

建立 `src/SoloLeveling.Domain/Entities/OwnedCard.cs`：

```csharp
namespace SoloLeveling.Domain.Entities;

/// <summary>
/// 使用者擁有的卡片；同一使用者同一張卡只有一列，重複抽到只加張數。
/// </summary>
public class OwnedCard
{
    /// <summary>所屬使用者（複合主鍵之一）。</summary>
    public Guid UserId { get; set; }

    /// <summary>卡片 ID（複合主鍵之一），對應 <c>Cards.All</c>。</summary>
    public string CardId { get; set; } = string.Empty;

    /// <summary>持有張數，至少 1。</summary>
    public int Count { get; set; }

    /// <summary>首次取得時間（UTC）。</summary>
    public DateTimeOffset FirstAcquiredAt { get; set; }
}
```

建立 `src/SoloLeveling.Domain/Entities/Achievement.cs`：

```csharp
namespace SoloLeveling.Domain.Entities;

/// <summary>
/// 已解鎖的成就；主鍵 (UserId, Key) 擋重複解鎖。
/// </summary>
public class Achievement
{
    /// <summary>所屬使用者（複合主鍵之一）。</summary>
    public Guid UserId { get; set; }

    /// <summary>成就鍵（複合主鍵之一），對應 <c>Achievements.All</c>。</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>解鎖時間（UTC）。</summary>
    public DateTimeOffset UnlockedAt { get; set; }
}
```

建立 `src/SoloLeveling.Domain/Entities/OwnedTheme.cs`：

```csharp
namespace SoloLeveling.Domain.Entities;

/// <summary>
/// 已購買的主題；預設主題 azure 不需要列。
/// </summary>
public class OwnedTheme
{
    /// <summary>所屬使用者（複合主鍵之一）。</summary>
    public Guid UserId { get; set; }

    /// <summary>主題鍵（複合主鍵之一）。</summary>
    public string ThemeKey { get; set; } = string.Empty;
}
```

建立 `src/SoloLeveling.Domain/Entities/CoinEvent.cs`：

```csharp
namespace SoloLeveling.Domain.Entities;

/// <summary>
/// 金幣流水；<see cref="Player.Coins"/> 的每一次變動都必須對應一筆事件。
/// </summary>
public class CoinEvent
{
    /// <summary>主鍵。</summary>
    public Guid Id { get; set; }

    /// <summary>DB 產生的遞增序號；同一請求內多筆事件的 <see cref="OccurredAt"/> 相同，靠此欄位穩定排序。</summary>
    public long Seq { get; set; }

    /// <summary>所屬使用者。</summary>
    public Guid UserId { get; set; }

    /// <summary>變動量；扣除為負值。</summary>
    public int Amount { get; set; }

    /// <summary>事件來源。</summary>
    public CoinSource Source { get; set; }

    /// <summary>關聯物件 ID：達標類指向 DailyLog，開箱類指向寶箱；成就與商店保險卡、主題為 null。</summary>
    public Guid? RefId { get; set; }

    /// <summary>發生時間（UTC）。</summary>
    public DateTimeOffset OccurredAt { get; set; }
}
```

建立 `src/SoloLeveling.Domain/Entities/RewardEvent.cs`：

```csharp
namespace SoloLeveling.Domain.Entities;

/// <summary>
/// 需要通知使用者的獎勵事件（目前只有保險卡生效）；可能由排程結算產生，下一次請求回報後寫入 <see cref="AnnouncedAt"/>。
/// </summary>
public class RewardEvent
{
    /// <summary>主鍵。</summary>
    public Guid Id { get; set; }

    /// <summary>所屬使用者。</summary>
    public Guid UserId { get; set; }

    /// <summary>事件種類。</summary>
    public RewardEventKind Kind { get; set; }

    /// <summary>事件對應的日期（使用者時區；保險卡為被保護的那一天）。</summary>
    public DateOnly Date { get; set; }

    /// <summary>發生時間（UTC）。</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>已回報給前端的時間（UTC）；null 表示尚未回報。</summary>
    public DateTimeOffset? AnnouncedAt { get; set; }
}
```

- [ ] **Step 5: 開箱與金幣規則**

建立 `src/SoloLeveling.Domain/Rules/Loot.cs`：

```csharp
namespace SoloLeveling.Domain.Rules;

/// <summary>
/// 開箱結果。
/// </summary>
/// <param name="CardId">抽到的卡片 ID。</param>
/// <param name="IsDuplicate">開箱前是否已擁有這張卡。</param>
/// <param name="BaseCoins">開箱固定金幣。</param>
/// <param name="DuplicateCoins">重複卡轉換的金幣；新卡為 0。</param>
public sealed record LootResult(string CardId, bool IsDuplicate, int BaseCoins, int DuplicateCoins)
{
    /// <summary>本次開箱共得金幣。</summary>
    public int Coins => BaseCoins + DuplicateCoins;
}

/// <summary>
/// 開箱規則：從寶箱等級的全部卡片中均勻抽一張（含已擁有），另附固定金幣；重複卡轉成金幣。
/// </summary>
public static class Loot
{
    /// <summary>
    /// 開箱固定金幣。
    /// </summary>
    /// <param name="rarity">寶箱等級。</param>
    /// <returns>E 20、C 50、A 100、S 300。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未知的稀有度。</exception>
    public static int OpenCoinsOf(Rarity rarity)
    {
        return rarity switch
        {
            Rarity.E => 20,
            Rarity.C => 50,
            Rarity.A => 100,
            Rarity.S => 300,
            _ => throw new ArgumentOutOfRangeException(nameof(rarity), rarity, "未知的稀有度"),
        };
    }

    /// <summary>
    /// 重複卡轉換的金幣。
    /// </summary>
    /// <param name="rarity">卡片稀有度。</param>
    /// <returns>E 30、C 80、A 200、S 500。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未知的稀有度。</exception>
    public static int DuplicateCoinsOf(Rarity rarity)
    {
        return rarity switch
        {
            Rarity.E => 30,
            Rarity.C => 80,
            Rarity.A => 200,
            Rarity.S => 500,
            _ => throw new ArgumentOutOfRangeException(nameof(rarity), rarity, "未知的稀有度"),
        };
    }

    /// <summary>
    /// 開一個寶箱；只計算結果，不修改任何實體。
    /// </summary>
    /// <param name="rarity">寶箱等級。</param>
    /// <param name="ownedCounts">開箱前已擁有的卡片張數（卡片 ID → 張數）。</param>
    /// <param name="rng">亂數來源（注入，測試可固定）。</param>
    /// <returns>抽到的卡片與金幣。</returns>
    public static LootResult Open(Rarity rarity, IReadOnlyDictionary<string, int> ownedCounts, Random rng)
    {
        var pool = Cards.OfRarity(rarity);
        var card = pool[rng.Next(pool.Count)];
        var isDuplicate = ownedCounts.TryGetValue(card.Id, out var count) && count > 0;
        return new LootResult(card.Id, isDuplicate, OpenCoinsOf(rarity), isDuplicate ? DuplicateCoinsOf(rarity) : 0);
    }
}
```

建立 `src/SoloLeveling.Domain/Rules/Wallet.cs`：

```csharp
using SoloLeveling.Domain.Entities;

namespace SoloLeveling.Domain.Rules;

/// <summary>
/// 金幣異動的唯一入口：改 <see cref="Player.Coins"/> 並回傳對應的 <see cref="CoinEvent"/>，由呼叫端持久化。
/// </summary>
public static class Wallet
{
    /// <summary>
    /// 增減金幣。
    /// </summary>
    /// <param name="player">玩家。</param>
    /// <param name="amount">變動量；扣除為負。</param>
    /// <param name="source">來源。</param>
    /// <param name="refId">關聯物件 ID。</param>
    /// <param name="now">當下時間（UTC）。</param>
    /// <returns>這次的金幣事件。</returns>
    /// <exception cref="InvalidOperationException">扣除後餘額會小於 0（呼叫端應先以 <see cref="Spend"/> 檢查或自行夾住金額）。</exception>
    public static CoinEvent Change(Player player, int amount, CoinSource source, Guid? refId, DateTimeOffset now)
    {
        if (player.Coins + amount < 0)
        {
            throw new InvalidOperationException($"金幣不可為負：餘額 {player.Coins}，變動 {amount}");
        }

        player.Coins += amount;
        return new CoinEvent { Id = Guid.NewGuid(), UserId = player.UserId, Amount = amount, Source = source, RefId = refId, OccurredAt = now };
    }

    /// <summary>
    /// 花費金幣；餘額不足時不扣款。
    /// </summary>
    /// <param name="player">玩家。</param>
    /// <param name="price">價格（正值）。</param>
    /// <param name="source">來源。</param>
    /// <param name="refId">關聯物件 ID。</param>
    /// <param name="now">當下時間（UTC）。</param>
    /// <returns>這次的金幣事件（金額為負）。</returns>
    /// <exception cref="DomainValidationException">餘額不足（錯誤碼 <c>NotEnoughCoins</c>）。</exception>
    public static CoinEvent Spend(Player player, int price, CoinSource source, Guid? refId, DateTimeOffset now)
    {
        if (player.Coins < price)
        {
            throw new DomainValidationException("NotEnoughCoins", $"金幣不足：需要 {price}，目前 {player.Coins}");
        }

        return Change(player, -price, source, refId, now);
    }
}
```

- [ ] **Step 6: 跑測試確認通過**

Run: `dotnet test tests/SoloLeveling.Domain.Tests`
Expected: 全部 PASS（含 `LootTests` 10 個、`WalletTests` 4 個）。

Run: `dotnet build && dotnet format --verify-no-changes`
Expected: 0 警告 0 錯誤、無格式差異（Api 尚未用到新欄位，EF 模型在 Task 5 才對應；此時 Api 測試不跑）。

- [ ] **Step 7: Commit**

```bash
git add src/SoloLeveling.Domain/Entities src/SoloLeveling.Domain/Rules/Loot.cs src/SoloLeveling.Domain/Rules/Wallet.cs tests/SoloLeveling.Domain.Tests/LootTests.cs tests/SoloLeveling.Domain.Tests/WalletTests.cs
git commit -F - <<'EOF'
feat: 新增獎勵實體、開箱抽卡與金幣異動規則

1. 寶箱、擁有卡片、成就、主題、金幣與獎勵事件各自成表，一次性項目以主鍵擋重複
2. 金幣只能經 Wallet 異動，每次都產生 CoinEvent，與 XpEvent 同一精神

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NxkXCvcUbg2y9cvwZjMor4
EOF
```

---

### Task 3: 結算時自動消耗連勝保險卡

**Files:**
- Modify: `src/SoloLeveling.Domain/Rules/Settlement.cs`（`SettlementResult` 加 `ShieldsUsed`；`Settle` 的未達標分支）
- Test: `tests/SoloLeveling.Domain.Tests/SettlementTests.cs`（檔尾新增 5 個測試）

**Interfaces:**
- Consumes: Task 2 的 `Player.ShieldCount`。
- Produces: `record SettlementResult(DateOnly Today, DailyLog TodayLog, IReadOnlyList<DailyLog> NewLogs, IReadOnlyList<XpEvent> Events, IReadOnlyList<DateOnly> ShieldsUsed)`；`Settlement.Settle` 簽章不變。

- [ ] **Step 1: 寫失敗的測試**

用 Edit 在 `tests/SoloLeveling.Domain.Tests/SettlementTests.cs` 類別最後一個 `}` 之前加入：

```csharp

    [Fact]
    public void Settle_漏一天且有保險卡_消耗1張且連勝延續()
    {
        var player = NewPlayer(lastSettled: Yesterday.AddDays(-1));
        player.Streak = 4;
        player.BestStreak = 4;
        player.ShieldCount = 2;

        var result = Settlement.Settle(player, Tz, [Log(Yesterday, 0.2m)], Now);

        player.ShieldCount.Should().Be(1);
        player.Streak.Should().Be(5);
        player.BestStreak.Should().Be(5);
        result.ShieldsUsed.Should().Equal(Yesterday);
    }

    [Fact]
    public void Settle_補多天_保險卡逐日消耗_用完後歸零()
    {
        // 待結算 Yesterday-3 到 Yesterday 共 4 天，全部缺席
        var player = NewPlayer(lastSettled: Yesterday.AddDays(-4));
        player.Streak = 3;
        player.ShieldCount = 2;

        var result = Settlement.Settle(player, Tz, [], Now);

        result.ShieldsUsed.Should().Equal(Yesterday.AddDays(-3), Yesterday.AddDays(-2));
        player.ShieldCount.Should().Be(0);
        player.Streak.Should().Be(0);
        player.BestStreak.Should().Be(5);
    }

    [Fact]
    public void Settle_Streak為0時未達標_不消耗保險卡()
    {
        var player = NewPlayer(lastSettled: Yesterday.AddDays(-1));
        player.ShieldCount = 1;

        var result = Settlement.Settle(player, Tz, [Log(Yesterday, 0m)], Now);

        player.ShieldCount.Should().Be(1);
        player.Streak.Should().Be(0);
        result.ShieldsUsed.Should().BeEmpty();
    }

    [Fact]
    public void Settle_達標日_不消耗保險卡()
    {
        var player = NewPlayer(lastSettled: Yesterday.AddDays(-1));
        player.Streak = 2;
        player.ShieldCount = 1;

        var result = Settlement.Settle(player, Tz, [Log(Yesterday, 0.8m)], Now);

        player.ShieldCount.Should().Be(1);
        player.Streak.Should().Be(3);
        result.ShieldsUsed.Should().BeEmpty();
    }

    [Fact]
    public void Settle_困難模式用保險卡_連勝延續但懲罰照扣()
    {
        var player = NewPlayer(lastSettled: Yesterday.AddDays(-1), hardMode: true);
        player.Streak = 2;
        player.ShieldCount = 1;
        player.Xp = 50;

        // 0.9 < 困難模式門檻 1.0
        var result = Settlement.Settle(player, Tz, [Log(Yesterday, 0.9m)], Now);

        player.Streak.Should().Be(3);
        player.ShieldCount.Should().Be(0);
        player.Xp.Should().Be(35);
        result.Events.Should().ContainSingle(e => e.Source == XpSource.Penalty && e.Amount == -15);
        result.ShieldsUsed.Should().Equal(Yesterday);
    }
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/SoloLeveling.Domain.Tests --filter "FullyQualifiedName~SettlementTests"`
Expected: 建置失敗，`SettlementResult` 不含 `ShieldsUsed` 的定義。

- [ ] **Step 3: 實作**

用 Edit 把 `src/SoloLeveling.Domain/Rules/Settlement.cs` 的

```csharp
/// <param name="Events">這次產生的 EXP 事件（只會有 Penalty）。</param>
public record SettlementResult(DateOnly Today, DailyLog TodayLog, IReadOnlyList<DailyLog> NewLogs, IReadOnlyList<XpEvent> Events);
```

換成：

```csharp
/// <param name="Events">這次產生的 EXP 事件（只會有 Penalty）。</param>
/// <param name="ShieldsUsed">這次消耗保險卡保護的日期，由舊到新；呼叫端據此寫 <c>RewardEvent</c>。</param>
public record SettlementResult(DateOnly Today, DailyLog TodayLog, IReadOnlyList<DailyLog> NewLogs, IReadOnlyList<XpEvent> Events, IReadOnlyList<DateOnly> ShieldsUsed);
```

用 Edit 把

```csharp
    /// <param name="player">玩家；Streak、BestStreak、Xp、LastSettledDate 會被更新。</param>
```

換成：

```csharp
    /// <param name="player">玩家；Streak、BestStreak、Xp、ShieldCount、LastSettledDate 會被更新。未達標日若連勝進行中且持有保險卡，消耗 1 張讓連勝延續（困難模式懲罰照扣）。</param>
```

用 Edit 把

```csharp
        var events = new List<XpEvent>();

        var start = player.LastSettledDate is { } last
```

換成：

```csharp
        var events = new List<XpEvent>();
        var shieldsUsed = new List<DateOnly>();

        var start = player.LastSettledDate is { } last
```

用 Edit 把

```csharp
            else
            {
                player.Streak = 0;
                if (player.HardMode && penaltiesApplied < MaxPenaltiesPerSettlement)
```

換成：

```csharp
            else
            {
                // 保險卡只保護進行中的連勝；Streak 為 0 時沒有東西可保護，不消耗
                if (player.ShieldCount > 0 && player.Streak > 0)
                {
                    player.ShieldCount -= 1;
                    player.Streak += 1;
                    shieldsUsed.Add(date);
                }
                else
                {
                    player.Streak = 0;
                }

                if (player.HardMode && penaltiesApplied < MaxPenaltiesPerSettlement)
```

用 Edit 把

```csharp
        return new SettlementResult(today, todayLog, newLogs, events);
```

換成：

```csharp
        return new SettlementResult(today, todayLog, newLogs, events, shieldsUsed);
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test tests/SoloLeveling.Domain.Tests`
Expected: 全部 PASS（既有 `SettlementTests` 不受影響，新增 5 個 PASS）。

Run: `dotnet build && dotnet format --verify-no-changes`
Expected: 0 警告 0 錯誤、無格式差異。

- [ ] **Step 5: Commit**

```bash
git add src/SoloLeveling.Domain/Rules/Settlement.cs tests/SoloLeveling.Domain.Tests/SettlementTests.cs
git commit -F - <<'EOF'
feat: 結算遇到未達標日自動消耗連勝保險卡

1. 結算本來就逐日判定達標，保險卡在同一迴圈處理，排程結算也一併生效
2. 只在連勝進行中才消耗，困難模式的 EXP 懲罰照扣

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NxkXCvcUbg2y9cvwZjMor4
EOF
```

---

### Task 4: 任務角色、目標完成判定與 `Rewards.Evaluate`

**Files:**
- Create: `src/SoloLeveling.Domain/Rules/QuestRoles.cs`
- Create: `src/SoloLeveling.Domain/Rules/GoalCompletion.cs`
- Create: `src/SoloLeveling.Domain/Rules/Rewards.cs`
- Test: `tests/SoloLeveling.Domain.Tests/QuestRolesTests.cs`、`tests/SoloLeveling.Domain.Tests/GoalCompletionTests.cs`、`tests/SoloLeveling.Domain.Tests/RewardsTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `Achievements`、`AchievementStats`、`AchievementDefinition`、`Shop.MaxShields`、enum；Task 2 的 `Player.PeakLevel／Coins／ShieldCount`、`DailyLog.BonusGranted／ClearCoinsGranted`；既有 `Leveling.RankOf`、`Progression.DefaultDaysPerStep`。
- Produces:
  - `QuestRoles.StreakWindowDays = 30`、`QuestRoles.Of(GoalCategory category, ProgressionValueKind? valueKind) → QuestRole?`、`QuestRoles.StreakOf(IReadOnlySet<DateOnly> doneDates, DateOnly today) → int`、`QuestRoles.CountsMinutes(QuestRole role) → bool`
  - `GoalCompletion.DoneDaysToFinish(Quest quest) → int`、`GoalCompletion.IsFinished(IReadOnlyCollection<Quest> activeQuests, Func<Quest, int> doneDaysIncludingToday) → bool`
  - `record PlayerSnapshot(int Level)`、`record ChestGrant(Rarity Rarity, ChestSource Source)`、`record CoinGrant(int Amount, CoinSource Source)`
  - `record RewardInput(PlayerSnapshot Before, Player Player, DailyLog TodayLog, IReadOnlySet<string> UnlockedKeys, AchievementStats Stats, int CompletedGoals, int CompletedPrograms)`
  - `record RewardOutcome(int LevelsGained, int NewPeakLevel, IReadOnlyList<string> RankUps, IReadOnlyList<ChestGrant> Chests, IReadOnlyList<AchievementDefinition> NewAchievements, IReadOnlyList<CoinGrant> Coins, int ShieldsGained, bool ClearCoinsGranted)`
  - `Rewards.DailyClearCoins = 10`、`Rewards.AchievementCoins = 50`、`Rewards.Evaluate(RewardInput input) → RewardOutcome`（純函式，不修改傳入實體）

- [ ] **Step 1: 寫失敗的測試**

建立 `tests/SoloLeveling.Domain.Tests/QuestRolesTests.cs`：

```csharp
using FluentAssertions;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class QuestRolesTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    [Theory]
    [InlineData(GoalCategory.Routine, ProgressionValueKind.TimeOfDay, QuestRole.Bedtime)]
    [InlineData(GoalCategory.Routine, ProgressionValueKind.TimeOfDayEvening, QuestRole.WakeUp)]
    [InlineData(GoalCategory.Exercise, ProgressionValueKind.Number, QuestRole.Exercise)]
    [InlineData(GoalCategory.Reading, ProgressionValueKind.Number, QuestRole.Reading)]
    [InlineData(GoalCategory.ScreenTime, ProgressionValueKind.Number, QuestRole.ScreenTime)]
    public void Of_由類別與值種類推導角色(GoalCategory category, ProgressionValueKind kind, QuestRole expected)
    {
        QuestRoles.Of(category, kind).Should().Be(expected);
    }

    [Fact]
    public void Of_作息但值種類不是時間_沒有角色()
    {
        QuestRoles.Of(GoalCategory.Routine, ProgressionValueKind.Number).Should().BeNull();
    }

    [Fact]
    public void StreakOf_今天已完成_連同前兩天算3()
    {
        QuestRoles.StreakOf(new HashSet<DateOnly> { Today, Today.AddDays(-1), Today.AddDays(-2) }, Today).Should().Be(3);
    }

    [Fact]
    public void StreakOf_今天未完成_算到昨天為止()
    {
        QuestRoles.StreakOf(new HashSet<DateOnly> { Today.AddDays(-1), Today.AddDays(-2) }, Today).Should().Be(2);
    }

    [Fact]
    public void StreakOf_中間斷一天就停()
    {
        QuestRoles.StreakOf(new HashSet<DateOnly> { Today, Today.AddDays(-2), Today.AddDays(-3) }, Today).Should().Be(1);
        QuestRoles.StreakOf(new HashSet<DateOnly>(), Today).Should().Be(0);
    }

    [Theory]
    [InlineData(QuestRole.Exercise, true)]
    [InlineData(QuestRole.Reading, true)]
    [InlineData(QuestRole.Bedtime, false)]
    [InlineData(QuestRole.WakeUp, false)]
    [InlineData(QuestRole.ScreenTime, false)]
    public void CountsMinutes_只有運動與閱讀累計分鐘(QuestRole role, bool expected)
    {
        QuestRoles.CountsMinutes(role).Should().Be(expected);
    }
}
```

建立 `tests/SoloLeveling.Domain.Tests/GoalCompletionTests.cs`：

```csharp
using FluentAssertions;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class GoalCompletionTests
{
    private static Quest Staged(int stageCount, int daysPerStep)
    {
        return new Quest { Id = Guid.NewGuid(), GoalId = Guid.NewGuid(), StageCount = stageCount, DaysPerStep = daysPerStep };
    }

    [Theory]
    [InlineData(3, 3, 7)]
    [InlineData(10, 3, 28)]
    [InlineData(8, 4, 29)]
    [InlineData(1, 3, 1)]
    public void DoneDaysToFinish_在最後一階達標一天(int stageCount, int daysPerStep, int expected)
    {
        GoalCompletion.DoneDaysToFinish(Staged(stageCount, daysPerStep)).Should().Be(expected);
    }

    [Fact]
    public void IsFinished_每個任務都在最後一階達標過才算完成()
    {
        var reading = Staged(3, 3);
        var wake = Staged(1, 3);
        var days = new Dictionary<Guid, int> { [reading.Id] = 7, [wake.Id] = 0 };

        GoalCompletion.IsFinished([reading, wake], q => days[q.Id]).Should().BeFalse();

        days[wake.Id] = 1;
        GoalCompletion.IsFinished([reading, wake], q => days[q.Id]).Should().BeTrue();
    }

    [Fact]
    public void IsFinished_只有一階_建立當下不算完成()
    {
        GoalCompletion.IsFinished([Staged(1, 3)], _ => 0).Should().BeFalse();
    }

    [Fact]
    public void IsFinished_沒有未封存任務_不算完成()
    {
        GoalCompletion.IsFinished([], _ => 100).Should().BeFalse();
    }
}
```

建立 `tests/SoloLeveling.Domain.Tests/RewardsTests.cs`：

```csharp
using FluentAssertions;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class RewardsTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static Player NewPlayer(int level = 1, int peakLevel = 1, int coins = 0, int shields = 0)
    {
        return new Player { UserId = UserId, Level = level, PeakLevel = peakLevel, Coins = coins, ShieldCount = shields };
    }

    private static DailyLog NewLog(bool bonusGranted = false, bool clearCoinsGranted = false)
    {
        return new DailyLog { Id = Guid.NewGuid(), UserId = UserId, BonusGranted = bonusGranted, ClearCoinsGranted = clearCoinsGranted };
    }

    private static RewardInput Input(
        Player player,
        int beforeLevel = 1,
        DailyLog? log = null,
        AchievementStats? stats = null,
        string[]? unlocked = null,
        int goals = 0,
        int programs = 0)
    {
        return new RewardInput(
            new PlayerSnapshot(beforeLevel),
            player,
            log ?? NewLog(),
            new HashSet<string>(unlocked ?? Array.Empty<string>()),
            stats ?? AchievementStats.Empty,
            goals,
            programs);
    }

    [Fact]
    public void Evaluate_升1級_發1個E箱並更新PeakLevel()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(level: 2), beforeLevel: 1));

        outcome.LevelsGained.Should().Be(1);
        outcome.NewPeakLevel.Should().Be(2);
        outcome.Chests.Should().Equal(new ChestGrant(Rarity.E, ChestSource.LevelUp));
        outcome.RankUps.Should().BeEmpty();
        outcome.ShieldsGained.Should().Be(0);
    }

    [Fact]
    public void Evaluate_一次跨2級_發2個E箱()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(level: 3), beforeLevel: 1));

        outcome.LevelsGained.Should().Be(2);
        outcome.Chests.Should().Equal(new ChestGrant(Rarity.E, ChestSource.LevelUp), new ChestGrant(Rarity.E, ChestSource.LevelUp));
    }

    [Fact]
    public void Evaluate_晉階E到D_另發C箱與1張保險卡()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(level: 5, peakLevel: 4), beforeLevel: 4));

        outcome.RankUps.Should().Equal("D");
        outcome.Chests.Should().Equal(new ChestGrant(Rarity.E, ChestSource.LevelUp), new ChestGrant(Rarity.C, ChestSource.RankUp));
        outcome.ShieldsGained.Should().Be(1);
    }

    [Fact]
    public void Evaluate_一次跨兩階_每階各發C箱與保險卡()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(level: 10, peakLevel: 4), beforeLevel: 4));

        outcome.RankUps.Should().Equal("D", "C");
        outcome.Chests.Count(c => c.Source == ChestSource.LevelUp).Should().Be(6);
        outcome.Chests.Count(c => c.Source == ChestSource.RankUp).Should().Be(2);
        outcome.ShieldsGained.Should().Be(2);
    }

    [Fact]
    public void Evaluate_晉階時已有3張保險卡_保險卡不超過上限但C箱照發()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(level: 5, peakLevel: 4, shields: 3), beforeLevel: 4));

        outcome.ShieldsGained.Should().Be(0);
        outcome.Chests.Should().Contain(new ChestGrant(Rarity.C, ChestSource.RankUp));
    }

    [Fact]
    public void Evaluate_撤銷後再升回曾到過的等級_不再發箱()
    {
        // 先前已到過 Lv2（PeakLevel 2），本次請求從 Lv1 升回 Lv2
        var outcome = Rewards.Evaluate(Input(NewPlayer(level: 2, peakLevel: 2), beforeLevel: 1));

        outcome.LevelsGained.Should().Be(1);
        outcome.NewPeakLevel.Should().Be(2);
        outcome.Chests.Should().BeEmpty();
        outcome.RankUps.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_無變化_結果為空()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer()));

        outcome.LevelsGained.Should().Be(0);
        outcome.NewPeakLevel.Should().Be(1);
        outcome.Chests.Should().BeEmpty();
        outcome.NewAchievements.Should().BeEmpty();
        outcome.Coins.Should().BeEmpty();
        outcome.ShieldsGained.Should().Be(0);
        outcome.ClearCoinsGranted.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_同請求撤銷再完成_等級與達標狀態不變_不發任何獎勵()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(), beforeLevel: 1, log: NewLog(bonusGranted: true, clearCoinsGranted: true)));

        outcome.Chests.Should().BeEmpty();
        outcome.Coins.Should().BeEmpty();
        outcome.ClearCoinsGranted.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_最佳連續7天_解鎖不屈並發C箱與50金幣()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(), stats: AchievementStats.Empty with { BestStreak = 7 }));

        outcome.NewAchievements.Select(a => a.Key).Should().Equal("streak-7");
        outcome.Chests.Should().Equal(new ChestGrant(Rarity.C, ChestSource.Streak7));
        outcome.Coins.Should().Equal(new CoinGrant(50, CoinSource.Achievement));
    }

    [Fact]
    public void Evaluate_不屈已解鎖_不再發C箱()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(), stats: AchievementStats.Empty with { BestStreak = 8 }, unlocked: ["streak-7"]));

        outcome.NewAchievements.Should().BeEmpty();
        outcome.Chests.Should().BeEmpty();
        outcome.Coins.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_最佳連續30天一次達成_解鎖不屈與恆心並發C箱與A箱()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(), stats: AchievementStats.Empty with { BestStreak = 30 }));

        outcome.NewAchievements.Select(a => a.Key).Should().Equal("streak-7", "streak-30");
        outcome.Chests.Should().Equal(new ChestGrant(Rarity.C, ChestSource.Streak7), new ChestGrant(Rarity.A, ChestSource.Streak30));
        outcome.Coins.Sum(c => c.Amount).Should().Be(100);
    }

    [Fact]
    public void Evaluate_完成2個目標_發2個A箱()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(), goals: 2));

        outcome.Chests.Should().Equal(new ChestGrant(Rarity.A, ChestSource.GoalCompleted), new ChestGrant(Rarity.A, ChestSource.GoalCompleted));
    }

    [Fact]
    public void Evaluate_完成66天週期_發S箱()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(), programs: 1));

        outcome.Chests.Should().Equal(new ChestGrant(Rarity.S, ChestSource.ProgramCompleted));
    }

    [Fact]
    public void Evaluate_分類連續30天_解鎖對應字塊並加50金幣()
    {
        var stats = AchievementStats.Empty with { RoleStreaks = new Dictionary<QuestRole, int> { [QuestRole.Bedtime] = 30 } };

        var outcome = Rewards.Evaluate(Input(NewPlayer(), stats: stats));

        outcome.NewAchievements.Select(a => a.Key).Should().Equal("bed-30");
        outcome.Coins.Should().Equal(new CoinGrant(50, CoinSource.Achievement));
        outcome.Chests.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_今日首次達標_加10金幣()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(), log: NewLog(bonusGranted: true)));

        outcome.Coins.Should().Equal(new CoinGrant(10, CoinSource.DailyClear));
        outcome.ClearCoinsGranted.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_撤銷達標_收回10金幣()
    {
        var outcome = Rewards.Evaluate(Input(NewPlayer(coins: 30), log: NewLog(bonusGranted: false, clearCoinsGranted: true)));

        outcome.Coins.Should().Equal(new CoinGrant(-10, CoinSource.DailyClearUndo));
        outcome.ClearCoinsGranted.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_撤銷達標但餘額不足_只扣到0()
    {
        var partial = Rewards.Evaluate(Input(NewPlayer(coins: 4), log: NewLog(bonusGranted: false, clearCoinsGranted: true)));
        partial.Coins.Should().Equal(new CoinGrant(-4, CoinSource.DailyClearUndo));
        partial.ClearCoinsGranted.Should().BeFalse();

        var empty = Rewards.Evaluate(Input(NewPlayer(coins: 0), log: NewLog(bonusGranted: false, clearCoinsGranted: true)));
        empty.Coins.Should().BeEmpty();
        empty.ClearCoinsGranted.Should().BeFalse();
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/SoloLeveling.Domain.Tests --filter "FullyQualifiedName~QuestRolesTests|FullyQualifiedName~GoalCompletionTests|FullyQualifiedName~RewardsTests"`
Expected: 建置失敗，`QuestRoles`、`GoalCompletion`、`Rewards`、`RewardInput`、`PlayerSnapshot`、`ChestGrant`、`CoinGrant` 不存在。

- [ ] **Step 3: 任務角色**

建立 `src/SoloLeveling.Domain/Rules/QuestRoles.cs`：

```csharp
namespace SoloLeveling.Domain.Rules;

/// <summary>
/// 由漸進任務推導角色（不新增欄位），以及分類成就用的連續天數算法。一般任務沒有角色。
/// </summary>
public static class QuestRoles
{
    /// <summary>計算分類連續天數時往前看的天數；成就門檻最多 30 天，查 [today − 30, today] 即足夠。</summary>
    public const int StreakWindowDays = 30;

    /// <summary>
    /// 由目標類別與值種類推導角色。
    /// </summary>
    /// <param name="category">任務所屬目標的類別。</param>
    /// <param name="valueKind">任務的值種類。</param>
    /// <returns>角色；無法對應時為 null。</returns>
    public static QuestRole? Of(GoalCategory category, ProgressionValueKind? valueKind)
    {
        return category switch
        {
            GoalCategory.Routine when valueKind == ProgressionValueKind.TimeOfDay => QuestRole.Bedtime,
            GoalCategory.Routine when valueKind == ProgressionValueKind.TimeOfDayEvening => QuestRole.WakeUp,
            GoalCategory.Exercise => QuestRole.Exercise,
            GoalCategory.Reading => QuestRole.Reading,
            GoalCategory.ScreenTime => QuestRole.ScreenTime,
            _ => null,
        };
    }

    /// <summary>
    /// 連續天數：到昨天為止連續幾天完成，今天已完成則再 +1。
    /// </summary>
    /// <param name="doneDates">該任務完成的日期（至少涵蓋 [today − <see cref="StreakWindowDays"/>, today]）。</param>
    /// <param name="today">使用者時區的今日。</param>
    /// <returns>連續天數。</returns>
    public static int StreakOf(IReadOnlySet<DateOnly> doneDates, DateOnly today)
    {
        var streak = doneDates.Contains(today) ? 1 : 0;
        for (var date = today.AddDays(-1); doneDates.Contains(date); date = date.AddDays(-1))
        {
            streak += 1;
        }

        return streak;
    }

    /// <summary>
    /// 該角色是否累計分鐘（運動、閱讀的進度值即分鐘數）。
    /// </summary>
    /// <param name="role">角色。</param>
    /// <returns>是否累計分鐘。</returns>
    public static bool CountsMinutes(QuestRole role)
    {
        return role is QuestRole.Exercise or QuestRole.Reading;
    }
}
```

- [ ] **Step 4: 目標完成判定**

建立 `src/SoloLeveling.Domain/Rules/GoalCompletion.cs`：

```csharp
using SoloLeveling.Domain.Entities;

namespace SoloLeveling.Domain.Rules;

/// <summary>
/// 目標完成判定：目標的每個未封存任務都在最後一階至少達標一天。
/// </summary>
public static class GoalCompletion
{
    /// <summary>
    /// 任務要累積幾天達標（含今天）才算在最後一階達標過：<c>(StageCount − 1) × DaysPerStep + 1</c>。
    /// </summary>
    /// <param name="quest">漸進任務。</param>
    /// <returns>需要的達標天數。</returns>
    public static int DoneDaysToFinish(Quest quest)
    {
        var stageCount = quest.StageCount ?? 1;
        var daysPerStep = quest.DaysPerStep ?? Progression.DefaultDaysPerStep;
        return ((stageCount - 1) * daysPerStep) + 1;
    }

    /// <summary>
    /// 目標是否完成。
    /// </summary>
    /// <param name="activeQuests">目標底下未封存的任務；空集合（任務都被單獨封存）不算完成。</param>
    /// <param name="doneDaysIncludingToday">取某任務的達標天數（今天之前＋今天是否完成）。</param>
    /// <returns>是否完成。</returns>
    public static bool IsFinished(IReadOnlyCollection<Quest> activeQuests, Func<Quest, int> doneDaysIncludingToday)
    {
        return activeQuests.Count > 0 && activeQuests.All(q => doneDaysIncludingToday(q) >= DoneDaysToFinish(q));
    }
}
```

- [ ] **Step 5: 獎勵判定**

建立 `src/SoloLeveling.Domain/Rules/Rewards.cs`：

```csharp
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
```

- [ ] **Step 6: 跑測試確認通過**

Run: `dotnet test tests/SoloLeveling.Domain.Tests`
Expected: 全部 PASS（`QuestRolesTests` 14、`GoalCompletionTests` 7、`RewardsTests` 17）。

Run: `dotnet build && dotnet format --verify-no-changes`
Expected: 0 警告 0 錯誤、無格式差異。

- [ ] **Step 7: Commit**

```bash
git add src/SoloLeveling.Domain/Rules/QuestRoles.cs src/SoloLeveling.Domain/Rules/GoalCompletion.cs src/SoloLeveling.Domain/Rules/Rewards.cs tests/SoloLeveling.Domain.Tests/QuestRolesTests.cs tests/SoloLeveling.Domain.Tests/GoalCompletionTests.cs tests/SoloLeveling.Domain.Tests/RewardsTests.cs
git commit -F - <<'EOF'
feat: 新增獎勵判定、任務角色與目標完成規則

1. 升級與晉階改以 PeakLevel 判定，撤銷再完成不會重複發寶箱
2. 成就以狀態判定並擋重複，達標金幣收回最多扣到 0

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NxkXCvcUbg2y9cvwZjMor4
EOF
```

---

### Task 5: DB 對應、migration、結算寫入保險卡事件

**Files:**
- Modify: `src/SoloLeveling.Infrastructure/AppDbContext.cs`（DbSet 與 `OnModelCreating`）
- Create: `src/SoloLeveling.Infrastructure/Migrations/<timestamp>_AddRewards.cs`（與 `.Designer.cs`、Snapshot 更新，由 `dotnet ef` 產生後手改兩處）
- Modify: `src/SoloLeveling.Infrastructure/SettlementService.cs`（寫 `RewardEvents`）
- Test: `tests/SoloLeveling.Api.Tests/SettlementServiceTests.cs`（`SeedUserAsync` 加參數、新增 1 個測試）

**Interfaces:**
- Consumes: Task 2 的實體；Task 3 的 `SettlementResult.ShieldsUsed`。
- Produces: `AppDbContext.RewardChests`、`OwnedCards`、`Achievements`、`OwnedThemes`、`CoinEvents`、`RewardEvents`（DbSet）；`SettlementService.SettleAsync` 會為每個 `ShieldsUsed` 日期加一筆 `RewardEvent { Kind = ShieldUsed, AnnouncedAt = null }`。

- [ ] **Step 1: 寫失敗的測試**

用 Edit 把 `tests/SoloLeveling.Api.Tests/SettlementServiceTests.cs` 的

```csharp
    private async Task<Guid> SeedUserAsync(string timeZoneId, DateOnly? lastSettled, bool hardMode = false, int streak = 0, int level = 1, int xp = 0)
    {
        await using var db = fixture.CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@test.local", PasswordHash = "x", DisplayName = "t", TimeZoneId = timeZoneId, CreatedAt = Now.AddDays(-30) };
        db.Users.Add(user);
        db.Players.Add(new Player { UserId = user.Id, LastSettledDate = lastSettled, HardMode = hardMode, Streak = streak, Level = level, Xp = xp, CreatedAt = user.CreatedAt });
```

換成：

```csharp
    private async Task<Guid> SeedUserAsync(string timeZoneId, DateOnly? lastSettled, bool hardMode = false, int streak = 0, int level = 1, int xp = 0, int shieldCount = 0)
    {
        await using var db = fixture.CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@test.local", PasswordHash = "x", DisplayName = "t", TimeZoneId = timeZoneId, CreatedAt = Now.AddDays(-30) };
        db.Users.Add(user);
        db.Players.Add(new Player { UserId = user.Id, LastSettledDate = lastSettled, HardMode = hardMode, Streak = streak, Level = level, Xp = xp, ShieldCount = shieldCount, CreatedAt = user.CreatedAt });
```

在同檔類別最後一個 `}` 之前加入：

```csharp

    [Fact]
    public async Task 漏一天且有保險卡_寫入未公告的ShieldUsed事件()
    {
        var userId = await SeedUserAsync("UTC", lastSettled: Yesterday.AddDays(-1), streak: 3, shieldCount: 1);

        await SettleAsync(userId, Now);

        var player = await LoadPlayerAsync(userId);
        player.Streak.Should().Be(4);
        player.ShieldCount.Should().Be(0);
        await using var db = fixture.CreateDbContext();
        var events = await db.RewardEvents.Where(e => e.UserId == userId).ToListAsync();
        events.Should().ContainSingle(e => e.Kind == RewardEventKind.ShieldUsed && e.Date == Yesterday && e.AnnouncedAt == null);
    }
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/SoloLeveling.Api.Tests --filter "FullyQualifiedName~SettlementServiceTests"`
Expected: 建置失敗，`AppDbContext` 不含 `RewardEvents` 的定義。

- [ ] **Step 3: DbContext 對應**

用 Edit 在 `src/SoloLeveling.Infrastructure/AppDbContext.cs` 的

```csharp
    /// <summary>EXP 流水。</summary>
    public DbSet<XpEvent> XpEvents => Set<XpEvent>();
```

之後加入：

```csharp

    /// <summary>寶箱。</summary>
    public DbSet<RewardChest> RewardChests => Set<RewardChest>();

    /// <summary>擁有的卡片。</summary>
    public DbSet<OwnedCard> OwnedCards => Set<OwnedCard>();

    /// <summary>已解鎖的成就。</summary>
    public DbSet<Achievement> Achievements => Set<Achievement>();

    /// <summary>已購買的主題。</summary>
    public DbSet<OwnedTheme> OwnedThemes => Set<OwnedTheme>();

    /// <summary>金幣流水。</summary>
    public DbSet<CoinEvent> CoinEvents => Set<CoinEvent>();

    /// <summary>需要通知使用者的獎勵事件。</summary>
    public DbSet<RewardEvent> RewardEvents => Set<RewardEvent>();
```

用 Edit 把

```csharp
        modelBuilder.Entity<Player>(b =>
        {
            b.HasKey(x => x.UserId);
            b.HasOne<User>().WithOne().HasForeignKey<Player>(x => x.UserId);
        });
```

換成：

```csharp
        modelBuilder.Entity<Player>(b =>
        {
            b.HasKey(x => x.UserId);
            b.HasOne<User>().WithOne().HasForeignKey<Player>(x => x.UserId);
            b.Property(x => x.ThemeKey).HasMaxLength(16).IsRequired();
            b.Property(x => x.TitlePrefixKey).HasMaxLength(32);
            b.Property(x => x.TitleSuffixKey).HasMaxLength(32);
            b.Property(x => x.PinnedCardId).HasMaxLength(64);
        });
```

用 Edit 把

```csharp
        modelBuilder.Entity<XpEvent>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            b.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.Seq).UseIdentityAlwaysColumn();
            b.HasIndex(x => new { x.UserId, x.OccurredAt });
        });
```

換成：

```csharp
        modelBuilder.Entity<XpEvent>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            b.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.Seq).UseIdentityAlwaysColumn();
            b.HasIndex(x => new { x.UserId, x.OccurredAt });
        });

        modelBuilder.Entity<RewardChest>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            b.Property(x => x.Rarity).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.DroppedCardId).HasMaxLength(64);
            b.HasIndex(x => new { x.UserId, x.OpenedAt });
        });

        modelBuilder.Entity<OwnedCard>(b =>
        {
            // 同一使用者同一張卡只有一列
            b.HasKey(x => new { x.UserId, x.CardId });
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            b.Property(x => x.CardId).HasMaxLength(64);
        });

        modelBuilder.Entity<Achievement>(b =>
        {
            // 解鎖一次性
            b.HasKey(x => new { x.UserId, x.Key });
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            b.Property(x => x.Key).HasMaxLength(32);
        });

        modelBuilder.Entity<OwnedTheme>(b =>
        {
            b.HasKey(x => new { x.UserId, x.ThemeKey });
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            b.Property(x => x.ThemeKey).HasMaxLength(16);
        });

        modelBuilder.Entity<CoinEvent>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            b.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.Seq).UseIdentityAlwaysColumn();
            b.HasIndex(x => new { x.UserId, x.OccurredAt });
        });

        modelBuilder.Entity<RewardEvent>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            b.HasIndex(x => new { x.UserId, x.AnnouncedAt });
        });
```

- [ ] **Step 4: 結算寫入保險卡事件**

用 Edit 把 `src/SoloLeveling.Infrastructure/SettlementService.cs` 開頭的

```csharp
using Microsoft.EntityFrameworkCore;
using SoloLeveling.Domain.Rules;
```

換成：

```csharp
using Microsoft.EntityFrameworkCore;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;
```

用 Edit 把

```csharp
            db.DailyLogs.AddRange(result.NewLogs);
            db.XpEvents.AddRange(result.Events);
            await db.SaveChangesAsync(ct);
```

換成：

```csharp
            db.DailyLogs.AddRange(result.NewLogs);
            db.XpEvents.AddRange(result.Events);
            // 保險卡可能由排程結算消耗，先記成未公告，下一次套用獎勵的請求再回報給前端
            db.RewardEvents.AddRange(result.ShieldsUsed.Select(date => new RewardEvent
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Kind = RewardEventKind.ShieldUsed,
                Date = date,
                OccurredAt = now,
            }));
            await db.SaveChangesAsync(ct);
```

- [ ] **Step 5: 產生 migration 並手改**

Run: `dotnet ef migrations add AddRewards -p src/SoloLeveling.Infrastructure -s src/SoloLeveling.Api -o Migrations`
Expected: `src/SoloLeveling.Infrastructure/Migrations/` 多出 `<timestamp>_AddRewards.cs` 與 `.Designer.cs`，`AppDbContextModelSnapshot.cs` 更新。打開 migration 確認 `Up` 有：`AddColumn` Players 的 `Coins`、`PeakLevel`、`PinnedCardId`、`ShieldCount`、`ThemeKey`、`TitlePrefixKey`、`TitleSuffixKey`；Goals／Programs 的 `CompletedAt`；DailyLogs 的 `ClearCoinsGranted`；`CreateTable` 六張表（`Achievements`、`CoinEvents`、`OwnedCards`、`OwnedThemes`、`RewardChests`、`RewardEvents`）與三個索引。

手改 1：把 Players `ThemeKey` 的 `AddColumn` 的 `defaultValue: ""` 改成 `defaultValue: "azure"`，讓既有玩家得到預設主題：

```csharp
            migrationBuilder.AddColumn<string>(
                name: "ThemeKey",
                table: "Players",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "azure");
```

手改 2：在 `Up` 方法的最後（所有 `CreateIndex` 之後、方法結尾 `}` 之前）加入：

```csharp

            // 既有玩家不補發歷史升級寶箱；上線前已達標的日子不補發達標金幣
            migrationBuilder.Sql("UPDATE \"Players\" SET \"PeakLevel\" = \"Level\";");
            migrationBuilder.Sql("UPDATE \"DailyLogs\" SET \"ClearCoinsGranted\" = \"BonusGranted\";");
```

- [ ] **Step 6: 跑測試確認通過**

Run: `dotnet test tests/SoloLeveling.Api.Tests --filter "FullyQualifiedName~SettlementServiceTests"`
Expected: 全部 PASS（`PostgresFixture` 套用新 migration；新增測試 PASS）。

Run: `dotnet test`
Expected: Domain 與 Api 全部 PASS（Api 端尚未呼叫獎勵，既有行為不變）。

Run: `dotnet format --verify-no-changes`
Expected: 無差異（`.editorconfig` 對 `Migrations/` 關閉 analyzer）。

- [ ] **Step 7: Commit**

```bash
git add src/SoloLeveling.Infrastructure tests/SoloLeveling.Api.Tests/SettlementServiceTests.cs
git commit -F - <<'EOF'
feat: 新增獎勵資料表 migration 並記錄保險卡生效事件

1. 六張新表與玩家、目標、週期、每日紀錄的新欄位，一次性項目以複合主鍵擋重複
2. 既有玩家的 PeakLevel 設為目前等級，避免上線後補發歷史升級寶箱

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NxkXCvcUbg2y9cvwZjMor4
EOF
```

---

### Task 6: Api 獎勵判定與持久化：`RewardApplier`，接到所有會改狀態的請求

**Files:**
- Create: `src/SoloLeveling.Api/Contracts/RewardDtos.cs`
- Modify: `src/SoloLeveling.Api/Contracts/Dtos.cs`（`PlayerDto`、`ProgramDto`、`MeResponse`、`QuestDto`）
- Modify: `src/SoloLeveling.Api/Contracts/TodayDtos.cs`（`TodayResponse`）
- Modify: `src/SoloLeveling.Api/Contracts/GoalDtos.cs`（`GoalsResponse`）
- Modify: `src/SoloLeveling.Api/Contracts/Mappers.cs`（玩家稱號與新欄位、寶箱／卡片／成就 mapper）
- Modify: `src/SoloLeveling.Api/Services/TodayContextLoader.cs`（`TodayContext.Before`）
- Create: `src/SoloLeveling.Api/Services/RewardStatsLoader.cs`
- Create: `src/SoloLeveling.Api/Services/RewardApplier.cs`
- Modify: `src/SoloLeveling.Api/Services/TodayService.cs`、`QuestService.cs`、`GoalService.cs`、`PlayerService.cs`、`ProgramService.cs`
- Modify: `src/SoloLeveling.Api/Controllers/QuestsController.cs`、`GoalsController.cs`（封存改回 200 `{ rewards }`）
- Modify: `src/SoloLeveling.Api/Program.cs`（DI）
- Test: Create `tests/SoloLeveling.Api.Tests/RewardsFlowApiTests.cs`；Modify `tests/SoloLeveling.Api.Tests/QuestsApiTests.cs`、`GoalsApiTests.cs`、`ProgressionApiTests.cs`（封存的狀態碼）

**Interfaces:**
- Consumes: Task 1–5 全部；既有 `TodayContextLoader.LoadAsync`、`TodayContext.DoneDaysOf(Guid)`、`ProgressUpdater`、`Mappers.ToUnixSeconds`。
- Produces:
  - `record ChestDto(Guid Id, Rarity Rarity, ChestSource Source, long CreatedAt)`
  - `record CardDto(string Id, string Name, Rarity Rarity, string Flavor, string Image)`
  - `record UnlockedAchievementDto(string Key, string Name, string TitleText, TitleSlot Slot)`
  - `record RewardsDto(int LevelsGained, List<string> RankUps, List<ChestDto> NewChests, List<UnlockedAchievementDto> NewAchievements, int CoinDelta, int ShieldsGained, List<DateOnly> ShieldsUsed, int ShieldCount)`
  - `record RewardsEnvelope(RewardsDto Rewards)`
  - `PlayerDto` 尾端新增 `string RankTitle, int Coins, int ShieldCount, string ThemeKey, CardDto? PinnedCard`；`Title` 改為 `Titles.Compose(...)` 的結果
  - `TodayResponse`、`MeResponse`、`GoalsResponse`、`QuestDto`、`ProgramDto` 尾端新增 `RewardsDto? Rewards = null`（null 時 JSON 省略）
  - `Mappers.ToDto(this RewardChest) → ChestDto`、`Mappers.ToDto(this CardDefinition) → CardDto`、`Mappers.ToUnlockedDto(this AchievementDefinition) → UnlockedAchievementDto`
  - `TodayContext` 尾端新增 `PlayerSnapshot Before`
  - `record RewardState(AchievementStats Stats, IReadOnlySet<string> UnlockedKeys)`；`RewardStatsLoader.LoadAsync(Player player, DateOnly today, CancellationToken ct) → Task<RewardState>`
  - `RewardApplier.ApplyAsync(TodayContext context, int completedPrograms, CancellationToken ct) → Task<RewardsDto>`（呼叫前必須已 `SaveChangesAsync`，方法內會再存一次；不 commit）
  - `QuestService.ArchiveAsync`、`GoalService.ArchiveAsync` 改回傳 `Task<RewardsDto>`

- [ ] **Step 1: 寫失敗的整合測試**

建立 `tests/SoloLeveling.Api.Tests/RewardsFlowApiTests.cs`：

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SoloLeveling.Api.Services;

namespace SoloLeveling.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class RewardsFlowApiTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture.ConnectionString);

    public void Dispose()
    {
        _factory.Dispose();
    }

    private static async Task<List<Guid>> QuestIdsAsync(HttpClient client)
    {
        var quests = await client.GetFromJsonAsync<JsonElement>("/api/v1/quests");
        return quests.EnumerateArray().Select(q => q.GetProperty("id").GetGuid()).ToList();
    }

    private static async Task<JsonElement> PutProgressAsync(HttpClient client, Guid questId, object? value)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/today/quests/{questId}/progress", new { value });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> UserIdAsync(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        return me.GetProperty("user").GetProperty("id").GetGuid();
    }

    private async Task SeedShieldsAsync(Guid userId, int count)
    {
        await using var db = fixture.CreateDbContext();
        var player = await db.Players.SingleAsync(p => p.UserId == userId);
        player.ShieldCount = count;
        await db.SaveChangesAsync();
    }

    /// <summary>勾 index 0、1、4、6、7 五個 Check，喝水 8 杯，最後螢幕時間 2.5 小時使今日 7/9 達標；回傳達標那一次的回應。</summary>
    private static async Task<JsonElement> CompleteSevenAsync(HttpClient client, List<Guid> ids)
    {
        foreach (var i in new[] { 0, 1, 4, 6, 7 })
        {
            await PutProgressAsync(client, ids[i], 1);
        }

        await PutProgressAsync(client, ids[2], 8);
        return await PutProgressAsync(client, ids[5], 2.5);
    }

    private static async Task<JsonElement> CreateGoalAsync(HttpClient client, object goal)
    {
        var response = await client.PostAsJsonAsync("/api/v1/goals", new { goals = new[] { goal }, basicQuestIndexes = Array.Empty<int>() });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<string?> AchievementKeys(JsonElement response)
    {
        return response.GetProperty("rewards").GetProperty("newAchievements").EnumerateArray().Select(a => a.GetProperty("key").GetString()).ToList();
    }

    private static List<string> Chests(JsonElement response)
    {
        return response.GetProperty("rewards").GetProperty("newChests").EnumerateArray()
            .Select(c => $"{c.GetProperty("rarity").GetString()}:{c.GetProperty("source").GetString()}")
            .ToList();
    }

    [Fact]
    public async Task 完成任務升到Lv2_回應含等級提升與E箱()
    {
        var client = await _factory.RegisterAsync();
        var ids = await QuestIdsAsync(client);

        await PutProgressAsync(client, ids[1], 1);
        await PutProgressAsync(client, ids[6], 1);
        // 三個 Hard 任務共 105 EXP，第三次跨過 Lv1 的 100
        var today = await PutProgressAsync(client, ids[5], 2.5);

        today.GetProperty("rewards").GetProperty("levelsGained").GetInt32().Should().Be(1);
        Chests(today).Should().Equal("E:LevelUp");
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        me.GetProperty("player").GetProperty("level").GetInt32().Should().Be(2);
        me.GetProperty("rewards").GetProperty("newChests").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task 升級後撤銷再完成_不重複發E箱()
    {
        var client = await _factory.RegisterAsync();
        var userId = await UserIdAsync(client);
        var ids = await QuestIdsAsync(client);
        await PutProgressAsync(client, ids[1], 1);
        await PutProgressAsync(client, ids[6], 1);
        Chests(await PutProgressAsync(client, ids[5], 2.5)).Should().Equal("E:LevelUp");

        // 螢幕時間 4 小時超過上限 → 撤銷 35 EXP，降回 Lv1
        var undo = await PutProgressAsync(client, ids[5], 4);
        Chests(undo).Should().BeEmpty();
        var redo = await PutProgressAsync(client, ids[5], 2.5);

        redo.GetProperty("rewards").GetProperty("levelsGained").GetInt32().Should().Be(1);
        Chests(redo).Should().BeEmpty();
        await using var db = fixture.CreateDbContext();
        (await db.RewardChests.CountAsync(c => c.UserId == userId)).Should().Be(1);
    }

    [Fact]
    public async Task 今日首次達標加10金幣_撤銷達標收回()
    {
        var client = await _factory.RegisterAsync();
        var ids = await QuestIdsAsync(client);

        var cleared = await CompleteSevenAsync(client, ids);
        cleared.GetProperty("isCleared").GetBoolean().Should().BeTrue();
        cleared.GetProperty("rewards").GetProperty("coinDelta").GetInt32().Should().Be(10);

        var undone = await PutProgressAsync(client, ids[5], 4);
        undone.GetProperty("isCleared").GetBoolean().Should().BeFalse();
        undone.GetProperty("rewards").GetProperty("coinDelta").GetInt32().Should().Be(-10);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        me.GetProperty("player").GetProperty("coins").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task 封存未完成任務使今日達標_回200並帶金幣_任務清單不帶rewards()
    {
        var client = await _factory.RegisterAsync();
        var ids = await QuestIdsAsync(client);
        foreach (var i in new[] { 0, 1, 4, 6, 7 })
        {
            await PutProgressAsync(client, ids[i], 1);
        }

        await PutProgressAsync(client, ids[2], 8);

        // 6/9 未達標；封存未完成的「三件好事」後 6/8 = 75%
        var response = await client.DeleteAsync($"/api/v1/quests/{ids[8]}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("rewards").GetProperty("coinDelta").GetInt32().Should().Be(10);
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/quests");
        list[0].TryGetProperty("rewards", out _).Should().BeFalse();
    }

    [Fact]
    public async Task 有保險卡時漏一天_連勝延續並在下次回應公告一次()
    {
        var client = await _factory.RegisterAsync();
        var userId = await UserIdAsync(client);
        await SeedShieldsAsync(userId, 1);
        await CompleteSevenAsync(client, await QuestIdsAsync(client));

        // 09-28 達標、09-29 漏掉、今天 09-30
        _factory.Clock.Advance(TimeSpan.FromDays(2));
        var today = await client.GetFromJsonAsync<JsonElement>("/api/v1/today");

        var rewards = today.GetProperty("rewards");
        rewards.GetProperty("shieldsUsed").EnumerateArray().Select(d => d.GetString()).Should().Equal("2026-09-29");
        rewards.GetProperty("shieldCount").GetInt32().Should().Be(0);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        me.GetProperty("player").GetProperty("displayStreak").GetInt32().Should().Be(2);
        me.GetProperty("player").GetProperty("shieldCount").GetInt32().Should().Be(0);
        me.GetProperty("rewards").GetProperty("shieldsUsed").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task 排程結算消耗保險卡_下次請求公告一次()
    {
        var client = await _factory.RegisterAsync();
        var userId = await UserIdAsync(client);
        await SeedShieldsAsync(userId, 1);
        await CompleteSevenAsync(client, await QuestIdsAsync(client));
        _factory.Clock.Advance(TimeSpan.FromDays(2));
        var scheduler = new SettlementScheduler(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.Clock,
            NullLogger<SettlementScheduler>.Instance);

        await scheduler.RunOnceAsync(CancellationToken.None);

        await using (var db = fixture.CreateDbContext())
        {
            (await db.Players.SingleAsync(p => p.UserId == userId)).ShieldCount.Should().Be(0);
            (await db.RewardEvents.SingleAsync(e => e.UserId == userId)).AnnouncedAt.Should().BeNull();
        }

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        me.GetProperty("rewards").GetProperty("shieldsUsed").EnumerateArray().Select(d => d.GetString()).Should().Equal("2026-09-29");
        var again = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        again.GetProperty("rewards").GetProperty("shieldsUsed").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task 目標完成發A箱且只發一次_建立第一個目標解鎖初次覺醒_連續7天解鎖不屈()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);
        // 閱讀 20 → 30 分鐘、7 天：3 階、每階 3 天，第 7 個達標日在最後一階
        var created = await CreateGoalAsync(client, new { category = "Reading", answers = new { currentMinutes = 20, targetMinutes = 30, lengthDays = 7 } });
        AchievementKeys(created).Should().Equal("first-goal");
        var readingId = created.GetProperty("goals")[0].GetProperty("quests")[0].GetProperty("id").GetGuid();

        for (var day = 1; day <= 6; day++)
        {
            var progress = await PutProgressAsync(client, readingId, 30);
            Chests(progress).Should().NotContain("A:GoalCompleted");
            _factory.Clock.Advance(TimeSpan.FromDays(1));
        }

        var day7 = await PutProgressAsync(client, readingId, 30);
        Chests(day7).Should().Contain(new[] { "A:GoalCompleted", "C:Streak7" });
        AchievementKeys(day7).Should().Contain("streak-7");

        _factory.Clock.Advance(TimeSpan.FromDays(1));
        var day8 = await PutProgressAsync(client, readingId, 30);
        Chests(day8).Should().NotContain("A:GoalCompleted");
        await using var db = fixture.CreateDbContext();
        var goalId = (await db.Quests.SingleAsync(q => q.Id == readingId)).GoalId!.Value;
        (await db.Goals.SingleAsync(g => g.Id == goalId)).CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task 開新週期_舊週期滿66天_發S箱並解鎖破繭()
    {
        var client = await _factory.RegisterAsync();
        _factory.Clock.Advance(TimeSpan.FromDays(66));

        var response = await client.PostAsync("/api/v1/program/restart", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("cycle").GetInt32().Should().Be(2);
        Chests(body).Should().Contain("S:ProgramCompleted");
        AchievementKeys(body).Should().Contain("program-complete");
    }

    [Fact]
    public async Task 開新週期_舊週期未滿66天_不發S箱()
    {
        var client = await _factory.RegisterAsync();
        _factory.Clock.Advance(TimeSpan.FromDays(65));

        var body = await (await client.PostAsync("/api/v1/program/restart", null)).Content.ReadFromJsonAsync<JsonElement>();

        Chests(body).Should().NotContain("S:ProgramCompleted");
        AchievementKeys(body).Should().NotContain("program-complete");
    }

    [Fact]
    public async Task 螢幕時間任務連續30天_第30天解鎖手機克星()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);
        var created = await CreateGoalAsync(client, new { category = "ScreenTime", answers = new { currentHours = 4, targetHours = 2, lengthDays = 30 } });
        var screenId = created.GetProperty("goals")[0].GetProperty("quests")[0].GetProperty("id").GetGuid();

        for (var day = 1; day <= 29; day++)
        {
            AchievementKeys(await PutProgressAsync(client, screenId, 0)).Should().NotContain("screen-30");
            _factory.Clock.Advance(TimeSpan.FromDays(1));
        }

        AchievementKeys(await PutProgressAsync(client, screenId, 0)).Should().Contain("screen-30");
    }

    [Fact]
    public async Task 閱讀任務累計1000分鐘_解鎖藏書()
    {
        var client = await _factory.RegisterAsync(seedBasicQuests: false);
        var created = await CreateGoalAsync(client, new { category = "Reading", answers = new { currentMinutes = 0, targetMinutes = 30, lengthDays = 30 } });
        var readingId = created.GetProperty("goals")[0].GetProperty("quests")[0].GetProperty("id").GetGuid();

        AchievementKeys(await PutProgressAsync(client, readingId, 999)).Should().NotContain("reading-1000");
        AchievementKeys(await PutProgressAsync(client, readingId, 1000)).Should().Contain("reading-1000");
    }
}
```

既有測試的封存狀態碼改成 200（三處，用 Edit）：

- `tests/SoloLeveling.Api.Tests/QuestsApiTests.cs` 的 `Delete_封存後不再出現在清單` 內：
  `response.StatusCode.Should().Be(HttpStatusCode.NoContent);` 緊接在 `var response = await client.DeleteAsync($"/api/v1/quests/{id}");` 之後的那一行，改成 `response.StatusCode.Should().Be(HttpStatusCode.OK);`（同檔 `Reorder_依questIds重排` 的 `NoContent` 不動）。
- `tests/SoloLeveling.Api.Tests/GoalsApiTests.cs`：`del.StatusCode.Should().Be(HttpStatusCode.NoContent);` 改成 `del.StatusCode.Should().Be(HttpStatusCode.OK);`。
- `tests/SoloLeveling.Api.Tests/ProgressionApiTests.cs`：`(await client.DeleteAsync($"/api/v1/quests/{bedId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);` 改成 `...Should().Be(HttpStatusCode.OK);`。

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/SoloLeveling.Api.Tests --filter "FullyQualifiedName~RewardsFlowApiTests"`
Expected: 編譯通過；全部 FAIL，錯誤為 `KeyNotFoundException`（回應沒有 `rewards`／`coins`／`shieldCount` 屬性）或 `Expected ... OK, but found NoContent`。

- [ ] **Step 3: DTO**

建立 `src/SoloLeveling.Api/Contracts/RewardDtos.cs`：

```csharp
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
```

用 Edit 修改 `src/SoloLeveling.Api/Contracts/Dtos.cs`：

1. 檔首 `using SoloLeveling.Domain;` 換成：

```csharp
using System.Text.Json.Serialization;
using SoloLeveling.Domain;
```

2. `/// <param name="Title">稱號。</param>` 換成：

```csharp
/// <param name="Title">顯示用稱號：有選字塊時為「前綴・後綴」組合，否則等於 <paramref name="RankTitle"/>。</param>
```

3. `PlayerDto` 這一行

```csharp
public record PlayerDto(int Level, int Xp, int XpNeeded, string Rank, string Title, StatsDto Stats, bool HardMode, int DisplayStreak, int BestStreak, int TotalCompleted);
```

換成：

```csharp
/// <param name="RankTitle">階級稱號（新手、見習者…）。</param>
/// <param name="Coins">金幣餘額。</param>
/// <param name="ShieldCount">連勝保險卡張數。</param>
/// <param name="ThemeKey">目前主題鍵（azure／violet／jade）。</param>
/// <param name="PinnedCard">釘選展示的卡片；未釘選為 null。</param>
public record PlayerDto(
    int Level,
    int Xp,
    int XpNeeded,
    string Rank,
    string Title,
    StatsDto Stats,
    bool HardMode,
    int DisplayStreak,
    int BestStreak,
    int TotalCompleted,
    string RankTitle,
    int Coins,
    int ShieldCount,
    string ThemeKey,
    CardDto? PinnedCard);
```

4. `ProgramDto` 這一行

```csharp
public record ProgramDto(DateOnly StartDate, int Cycle, int DayNumber, int LengthDays, bool IsCompleted);
```

換成：

```csharp
/// <param name="Rewards">本次請求的獎勵；只有 POST /program/restart 帶，其他地方為 null 並從 JSON 省略。</param>
public record ProgramDto(
    DateOnly StartDate,
    int Cycle,
    int DayNumber,
    int LengthDays,
    bool IsCompleted,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RewardsDto? Rewards = null);
```

5. `MeResponse` 這一行

```csharp
public record MeResponse(UserDto User, PlayerDto Player, ProgramDto Program, bool NeedsOnboarding);
```

換成：

```csharp
/// <param name="Rewards">本次請求的獎勵。</param>
public record MeResponse(
    UserDto User,
    PlayerDto Player,
    ProgramDto Program,
    bool NeedsOnboarding,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RewardsDto? Rewards = null);
```

6. `QuestDto` 這一行

```csharp
public record QuestDto(Guid Id, string Name, StatType StatType, Difficulty Difficulty, QuestType QuestType, decimal? TargetValue, decimal? Step, string? Unit, int SortOrder, Guid? GoalId);
```

換成：

```csharp
/// <param name="Rewards">本次請求的獎勵；只有 POST／PUT /quests 帶，清單中為 null 並從 JSON 省略。</param>
public record QuestDto(
    Guid Id,
    string Name,
    StatType StatType,
    Difficulty Difficulty,
    QuestType QuestType,
    decimal? TargetValue,
    decimal? Step,
    string? Unit,
    int SortOrder,
    Guid? GoalId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RewardsDto? Rewards = null);
```

用 Edit 修改 `src/SoloLeveling.Api/Contracts/TodayDtos.cs`：檔首 `using SoloLeveling.Domain;` 換成 `using System.Text.Json.Serialization;` 加一行 `using SoloLeveling.Domain;`；

```csharp
public record TodayResponse(DateOnly Date, decimal CompletionRatio, bool IsCleared, decimal Threshold, bool BonusGranted, string? Note, List<TodayQuestDto> Quests);
```

換成：

```csharp
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
```

用 Edit 修改 `src/SoloLeveling.Api/Contracts/GoalDtos.cs`：`using System.Text.Json;` 之後加一行 `using System.Text.Json.Serialization;`；

```csharp
public record GoalsResponse(List<GoalDto> Goals);
```

換成：

```csharp
/// <param name="Rewards">本次請求的獎勵；只有 POST /goals 帶，GET /goals 為 null 並從 JSON 省略。</param>
public record GoalsResponse(
    List<GoalDto> Goals,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RewardsDto? Rewards = null);
```

- [ ] **Step 4: Mapper**

用 Edit 修改 `src/SoloLeveling.Api/Contracts/Mappers.cs`：

1. 檔首 `using SoloLeveling.Domain.Entities;` 之前加一行 `using SoloLeveling.Domain;`。

2. 把

```csharp
    /// <summary>玩家狀態；displayStreak 依今日是否達標即時加 1。</summary>
    /// <param name="player">玩家實體。</param>
    /// <param name="todayCleared">今日是否達標。</param>
    /// <returns>DTO。</returns>
    public static PlayerDto ToDto(this Player player, bool todayCleared)
    {
        var rank = Leveling.RankOf(player.Level);
        return new PlayerDto(
            player.Level,
            player.Xp,
            Leveling.XpNeeded(player.Level),
            rank.Rank,
            rank.Title,
            new StatsDto(player.Str, player.Vit, player.Int, player.Wil, player.Spi),
            player.HardMode,
            player.Streak + (todayCleared ? 1 : 0),
            player.BestStreak,
            player.TotalCompleted);
    }
```

換成：

```csharp
    /// <summary>玩家狀態；displayStreak 依今日是否達標即時加 1，title 為組合後稱號（見 <see cref="Titles.Compose"/>）。</summary>
    /// <param name="player">玩家實體。</param>
    /// <param name="todayCleared">今日是否達標。</param>
    /// <returns>DTO。</returns>
    public static PlayerDto ToDto(this Player player, bool todayCleared)
    {
        var rank = Leveling.RankOf(player.Level);
        var pinned = player.PinnedCardId is { } cardId ? Cards.Find(cardId)?.ToDto() : null;
        return new PlayerDto(
            player.Level,
            player.Xp,
            Leveling.XpNeeded(player.Level),
            rank.Rank,
            Titles.Compose(player.TitlePrefixKey, player.TitleSuffixKey, player.Level),
            new StatsDto(player.Str, player.Vit, player.Int, player.Wil, player.Spi),
            player.HardMode,
            player.Streak + (todayCleared ? 1 : 0),
            player.BestStreak,
            player.TotalCompleted,
            rank.Title,
            player.Coins,
            player.ShieldCount,
            player.ThemeKey,
            pinned);
    }
```

3. 在 `/// <summary>時間戳轉 Unix 秒。</summary>` 之前加入：

```csharp
    /// <summary>寶箱。</summary>
    /// <param name="chest">寶箱實體。</param>
    /// <returns>DTO。</returns>
    public static ChestDto ToDto(this RewardChest chest)
    {
        return new ChestDto(chest.Id, chest.Rarity, chest.Source, chest.CreatedAt.ToUnixSeconds());
    }

    /// <summary>卡片目錄項目。</summary>
    /// <param name="card">卡片定義。</param>
    /// <returns>DTO。</returns>
    public static CardDto ToDto(this CardDefinition card)
    {
        return new CardDto(card.Id, card.Name, card.Rarity, card.Flavor, card.Image);
    }

    /// <summary>新解鎖的成就。</summary>
    /// <param name="achievement">成就定義。</param>
    /// <returns>DTO。</returns>
    public static UnlockedAchievementDto ToUnlockedDto(this AchievementDefinition achievement)
    {
        return new UnlockedAchievementDto(achievement.Key, achievement.Name, achievement.TitleText, achievement.Slot);
    }

```

- [ ] **Step 5: TodayContext 加快照**

用 Edit 修改 `src/SoloLeveling.Api/Services/TodayContextLoader.cs`：

1. 檔首 `using SoloLeveling.Domain.Entities;` 之後加一行 `using SoloLeveling.Domain.Rules;`。

2. 把

```csharp
/// <param name="DoneDaysBeforeToday">每個漸進任務在今天之前的達標天數；一般任務不在字典內。</param>
public sealed record TodayContext(
    User User,
    Player Player,
    DailyLog TodayLog,
    List<Quest> ActiveQuests,
    DateOnly Today,
    IReadOnlyDictionary<Guid, int> DoneDaysBeforeToday)
```

換成：

```csharp
/// <param name="DoneDaysBeforeToday">每個漸進任務在今天之前的達標天數；一般任務不在字典內。</param>
/// <param name="Before">結算後、任何修改前的玩家快照（在列鎖內拍；結算不改等級，等同請求前的值），供獎勵回應的 levelsGained 使用。</param>
public sealed record TodayContext(
    User User,
    Player Player,
    DailyLog TodayLog,
    List<Quest> ActiveQuests,
    DateOnly Today,
    IReadOnlyDictionary<Guid, int> DoneDaysBeforeToday,
    PlayerSnapshot Before)
```

3. 把

```csharp
        return new TodayContext(user, player, settled.TodayLog, activeQuests, settled.Today, doneDays);
```

換成：

```csharp
        return new TodayContext(user, player, settled.TodayLog, activeQuests, settled.Today, doneDays, new PlayerSnapshot(player.Level));
```

- [ ] **Step 6: 統計載入與獎勵套用服務**

建立 `src/SoloLeveling.Api/Services/RewardStatsLoader.cs`：

```csharp
using Microsoft.EntityFrameworkCore;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Services;

/// <summary>
/// 成就判定所需的統計與已解鎖成就。
/// </summary>
/// <param name="Stats">統計快照。</param>
/// <param name="UnlockedKeys">已解鎖的成就鍵。</param>
public sealed record RewardState(AchievementStats Stats, IReadOnlySet<string> UnlockedKeys);

/// <summary>
/// 批次查出成就統計（固定 7 次查詢，不隨任務數增加）。必須在本次請求的修改 SaveChanges 之後呼叫，查詢才看得到今天的進度與新建的目標。
/// </summary>
/// <param name="db">DbContext。</param>
public class RewardStatsLoader(AppDbContext db)
{
    /// <summary>
    /// 載入統計。
    /// </summary>
    /// <param name="player">玩家（已反映本次修改；BestStreak、TotalCompleted、等級直接取自此實體）。</param>
    /// <param name="today">使用者時區的今日。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>統計與已解鎖成就。</returns>
    public async Task<RewardState> LoadAsync(Player player, DateOnly today, CancellationToken ct)
    {
        var userId = player.UserId;
        var goalCategories = await db.Goals
            .Where(g => g.UserId == userId)
            .Select(g => new { g.Id, g.Category })
            .ToDictionaryAsync(g => g.Id, g => g.Category, ct);
        var goalQuests = await db.Quests
            .Where(q => q.UserId == userId && q.GoalId != null)
            .Select(q => new { q.Id, GoalId = q.GoalId!.Value, q.ValueKind, q.IsArchived })
            .ToListAsync(ct);
        var roles = goalQuests
            .Select(q => new { q.Id, q.IsArchived, Role = QuestRoles.Of(goalCategories[q.GoalId], q.ValueKind) })
            .Where(q => q.Role != null)
            .ToList();

        // 分類連續：只看未封存的角色任務，封存重建目標後以新任務重新起算
        var activeRoles = roles.Where(r => !r.IsArchived).ToList();
        var activeRoleIds = activeRoles.Select(r => r.Id).ToList();
        var since = today.AddDays(-QuestRoles.StreakWindowDays);
        var doneRows = activeRoleIds.Count == 0
            ? new List<DoneRow>()
            : await db.QuestProgresses
                .Join(db.DailyLogs, p => p.DailyLogId, l => l.Id, (p, l) => new { p.QuestId, p.IsDone, l.Date })
                .Where(x => x.IsDone && x.Date >= since && x.Date <= today && activeRoleIds.Contains(x.QuestId))
                .Select(x => new DoneRow(x.QuestId, x.Date))
                .ToListAsync(ct);
        var datesByQuest = doneRows.ToLookup(r => r.QuestId, r => r.Date);
        var roleStreaks = activeRoles
            .GroupBy(r => r.Role!.Value)
            .ToDictionary(g => g.Key, g => g.Max(r => QuestRoles.StreakOf(datesByQuest[r.Id].ToHashSet(), today)));

        // 分類累計分鐘：含已封存任務（終身累計）
        var minuteRoles = roles.Where(r => QuestRoles.CountsMinutes(r.Role!.Value)).ToList();
        var minuteIds = minuteRoles.Select(r => r.Id).ToList();
        var minutesByQuest = minuteIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await db.QuestProgresses
                .Where(p => minuteIds.Contains(p.QuestId) && p.Value != null)
                .GroupBy(p => p.QuestId)
                .Select(g => new { QuestId = g.Key, Total = g.Sum(p => p.Value!.Value) })
                .ToDictionaryAsync(x => x.QuestId, x => x.Total, ct);
        var roleMinutes = minuteRoles
            .GroupBy(r => r.Role!.Value)
            .ToDictionary(g => g.Key, g => (int)Math.Floor(g.Sum(r => minutesByQuest.GetValueOrDefault(r.Id))));

        var ownedCardKinds = await db.OwnedCards.CountAsync(c => c.UserId == userId, ct);
        var completedPrograms = await db.Programs.CountAsync(p => p.UserId == userId && p.CompletedAt != null, ct);
        var unlocked = await db.Achievements.Where(a => a.UserId == userId).Select(a => a.Key).ToListAsync(ct);

        var stats = new AchievementStats(
            goalCategories.Count,
            player.BestStreak,
            player.TotalCompleted,
            roleStreaks,
            roleMinutes,
            ownedCardKinds,
            completedPrograms,
            Math.Max(player.PeakLevel, player.Level));
        return new RewardState(stats, unlocked.ToHashSet());
    }

    private sealed record DoneRow(Guid QuestId, DateOnly Date);
}
```

建立 `src/SoloLeveling.Api/Services/RewardApplier.cs`：

```csharp
using Microsoft.EntityFrameworkCore;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Domain.Rules;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Services;

/// <summary>
/// 每個會改玩家狀態的請求在第一次 <c>SaveChangesAsync</c> 之後呼叫一次：判定目標完成、批次查統計、執行
/// <see cref="Rewards.Evaluate"/>，把寶箱、成就、金幣事件寫入並回報未公告的保險卡事件。仍在呼叫端的交易內，不 commit。
/// </summary>
/// <param name="db">DbContext。</param>
/// <param name="statsLoader">成就統計載入。</param>
/// <param name="clock">時間來源。</param>
public class RewardApplier(AppDbContext db, RewardStatsLoader statsLoader, TimeProvider clock)
{
    /// <summary>
    /// 判定並持久化本次請求的獎勵；方法結尾會再 SaveChanges 一次。
    /// </summary>
    /// <param name="context">今日內容（已反映本次修改並存檔）。</param>
    /// <param name="completedPrograms">本次請求新完成的 66 天週期數（只有開新週期會傳 1）。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>本次獎勵，放進回應的 <c>rewards</c> 欄位。</returns>
    public async Task<RewardsDto> ApplyAsync(TodayContext context, int completedPrograms, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var player = context.Player;
        var completedGoals = await CompleteGoalsAsync(context, now, ct);
        var state = await statsLoader.LoadAsync(player, context.Today, ct);
        var outcome = Rewards.Evaluate(new RewardInput(
            context.Before,
            player,
            context.TodayLog,
            state.UnlockedKeys,
            state.Stats,
            completedGoals,
            completedPrograms));

        player.PeakLevel = outcome.NewPeakLevel;
        player.ShieldCount += outcome.ShieldsGained;
        context.TodayLog.ClearCoinsGranted = outcome.ClearCoinsGranted;

        var chests = outcome.Chests
            .Select(c => new RewardChest { Id = Guid.NewGuid(), UserId = player.UserId, Rarity = c.Rarity, Source = c.Source, CreatedAt = now })
            .ToList();
        db.RewardChests.AddRange(chests);
        db.Achievements.AddRange(outcome.NewAchievements.Select(a => new Achievement { UserId = player.UserId, Key = a.Key, UnlockedAt = now }));
        foreach (var grant in outcome.Coins)
        {
            Guid? refId = grant.Source is CoinSource.DailyClear or CoinSource.DailyClearUndo ? context.TodayLog.Id : null;
            db.CoinEvents.Add(Wallet.Change(player, grant.Amount, grant.Source, refId, now));
        }

        var shieldEvents = await db.RewardEvents
            .Where(e => e.UserId == player.UserId && e.Kind == RewardEventKind.ShieldUsed && e.AnnouncedAt == null)
            .OrderBy(e => e.Date)
            .ToListAsync(ct);
        foreach (var shieldEvent in shieldEvents)
        {
            shieldEvent.AnnouncedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return new RewardsDto(
            outcome.LevelsGained,
            outcome.RankUps.ToList(),
            chests.Select(c => c.ToDto()).ToList(),
            outcome.NewAchievements.Select(a => a.ToUnlockedDto()).ToList(),
            outcome.Coins.Sum(c => c.Amount),
            outcome.ShieldsGained,
            shieldEvents.Select(e => e.Date).ToList(),
            player.ShieldCount);
    }

    /// <summary>
    /// 把本次達成完成條件的目標寫入 <see cref="Goal.CompletedAt"/>；已完成的不再判定，所以 A 級寶箱只發一次。
    /// </summary>
    /// <param name="context">今日內容。</param>
    /// <param name="now">當下時間（UTC）。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>本次新完成的目標數。</returns>
    private async Task<int> CompleteGoalsAsync(TodayContext context, DateTimeOffset now, CancellationToken ct)
    {
        var goals = await db.Goals
            .Where(g => g.UserId == context.User.Id && !g.IsArchived && g.CompletedAt == null)
            .ToListAsync(ct);
        var doneToday = context.TodayLog.Progresses.Where(p => p.IsDone).Select(p => p.QuestId).ToHashSet();
        var completed = 0;
        foreach (var goal in goals)
        {
            var quests = context.ActiveQuests.Where(q => q.GoalId == goal.Id).ToList();
            if (GoalCompletion.IsFinished(quests, q => context.DoneDaysOf(q.Id) + (doneToday.Contains(q.Id) ? 1 : 0)))
            {
                goal.CompletedAt = now;
                completed += 1;
            }
        }

        return completed;
    }
}
```

用 Edit 在 `src/SoloLeveling.Api/Program.cs` 的 `builder.Services.AddScoped<TodayContextLoader>();` 之後加入：

```csharp
builder.Services.AddScoped<RewardStatsLoader>();
builder.Services.AddScoped<RewardApplier>();
```

- [ ] **Step 7: 接到今日服務**

用 Edit 修改 `src/SoloLeveling.Api/Services/TodayService.cs`：

1. 把

```csharp
/// <param name="loader">今日內容載入（含結算）。</param>
/// <param name="clock">時間來源。</param>
public class TodayService(AppDbContext db, TodayContextLoader loader, TimeProvider clock)
```

換成：

```csharp
/// <param name="loader">今日內容載入（含結算）。</param>
/// <param name="rewardApplier">獎勵判定與持久化。</param>
/// <param name="clock">時間來源。</param>
public class TodayService(AppDbContext db, TodayContextLoader loader, RewardApplier rewardApplier, TimeProvider clock)
```

2. 把 `GetTodayAsync` 內的

```csharp
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        await tx.CommitAsync(ct);
        return Build(context);
```

換成：

```csharp
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        // 結算可能消耗了保險卡，也可能補解鎖排程造成的成就，GET 也套用一次
        var rewards = await rewardApplier.ApplyAsync(context, 0, ct);
        await tx.CommitAsync(ct);
        return Build(context) with { Rewards = rewards };
```

3. 把 `SetProgressAsync` 內的

```csharp
        db.XpEvents.AddRange(events);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Build(context);
```

換成：

```csharp
        db.XpEvents.AddRange(events);
        await db.SaveChangesAsync(ct);
        var rewards = await rewardApplier.ApplyAsync(context, 0, ct);
        await tx.CommitAsync(ct);
        return Build(context) with { Rewards = rewards };
```

- [ ] **Step 8: 接到任務服務**

用 Edit 修改 `src/SoloLeveling.Api/Services/QuestService.cs`：

1. 建構式（同 Step 7 第 1 點的作法）：

```csharp
/// <param name="loader">今日內容載入（含結算）。</param>
/// <param name="clock">時間來源。</param>
public class QuestService(AppDbContext db, TodayContextLoader loader, TimeProvider clock)
```

換成：

```csharp
/// <param name="loader">今日內容載入（含結算）。</param>
/// <param name="rewardApplier">獎勵判定與持久化。</param>
/// <param name="clock">時間來源。</param>
public class QuestService(AppDbContext db, TodayContextLoader loader, RewardApplier rewardApplier, TimeProvider clock)
```

2. `CreateAsync` 結尾

```csharp
        db.XpEvents.AddRange(ProgressUpdater.Recalculate(context.Player, context.TodayLog, context.ActiveQuests, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return quest.ToDto();
```

換成：

```csharp
        db.XpEvents.AddRange(ProgressUpdater.Recalculate(context.Player, context.TodayLog, context.ActiveQuests, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        var rewards = await rewardApplier.ApplyAsync(context, 0, ct);
        await tx.CommitAsync(ct);
        return quest.ToDto() with { Rewards = rewards };
```

3. `UpdateAsync` 漸進任務分支

```csharp
            quest.StatType = request.StatType;
            quest.Difficulty = request.Difficulty;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return quest.ToDto();
```

換成：

```csharp
            quest.StatType = request.StatType;
            quest.Difficulty = request.Difficulty;
            await db.SaveChangesAsync(ct);
            var lockedRewards = await rewardApplier.ApplyAsync(context, 0, ct);
            await tx.CommitAsync(ct);
            return quest.ToDto() with { Rewards = lockedRewards };
```

4. `UpdateAsync` 結尾

```csharp
        Apply(quest, request);
        db.XpEvents.AddRange(ProgressUpdater.Recalculate(context.Player, context.TodayLog, context.ActiveQuests, now));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return quest.ToDto();
```

換成：

```csharp
        Apply(quest, request);
        db.XpEvents.AddRange(ProgressUpdater.Recalculate(context.Player, context.TodayLog, context.ActiveQuests, now));
        await db.SaveChangesAsync(ct);
        var rewards = await rewardApplier.ApplyAsync(context, 0, ct);
        await tx.CommitAsync(ct);
        return quest.ToDto() with { Rewards = rewards };
```

5. `ArchiveAsync` 簽章

```csharp
    /// <returns>非同步作業。</returns>
    /// <exception cref="ApiErrorException">任務不存在或不屬於此使用者（404）。</exception>
    public async Task ArchiveAsync(Guid userId, Guid questId, CancellationToken ct)
```

換成：

```csharp
    /// <returns>本次獎勵（封存可能讓今日達標）。</returns>
    /// <exception cref="ApiErrorException">任務不存在或不屬於此使用者（404）。</exception>
    public async Task<RewardsDto> ArchiveAsync(Guid userId, Guid questId, CancellationToken ct)
```

6. `ArchiveAsync` 結尾

```csharp
        db.XpEvents.AddRange(ProgressUpdater.Recalculate(context.Player, context.TodayLog, context.ActiveQuests, now));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
```

換成：

```csharp
        db.XpEvents.AddRange(ProgressUpdater.Recalculate(context.Player, context.TodayLog, context.ActiveQuests, now));
        await db.SaveChangesAsync(ct);
        var rewards = await rewardApplier.ApplyAsync(context, 0, ct);
        await tx.CommitAsync(ct);
        return rewards;
    }
```

用 Edit 把 `src/SoloLeveling.Api/Controllers/QuestsController.cs` 的

```csharp
    /// <returns>204。</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await quests.ArchiveAsync(User.GetUserId(), id, ct);
        return NoContent();
    }
```

換成：

```csharp
    /// <returns>200 與本次獎勵。</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType<RewardsEnvelope>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RewardsEnvelope>> Archive(Guid id, CancellationToken ct)
    {
        return new RewardsEnvelope(await quests.ArchiveAsync(User.GetUserId(), id, ct));
    }
```

- [ ] **Step 9: 接到目標服務**

用 Edit 修改 `src/SoloLeveling.Api/Services/GoalService.cs`：

1. 建構式：

```csharp
/// <param name="loader">今日內容載入（含結算）。</param>
/// <param name="clock">時間來源。</param>
public class GoalService(AppDbContext db, TodayContextLoader loader, TimeProvider clock)
```

換成：

```csharp
/// <param name="loader">今日內容載入（含結算）。</param>
/// <param name="rewardApplier">獎勵判定與持久化。</param>
/// <param name="clock">時間來源。</param>
public class GoalService(AppDbContext db, TodayContextLoader loader, RewardApplier rewardApplier, TimeProvider clock)
```

2. `CreateAsync` 結尾

```csharp
        db.XpEvents.AddRange(ProgressUpdater.Recalculate(context.Player, context.TodayLog, context.ActiveQuests, now));
        await db.SaveChangesAsync(ct);
        var response = await BuildListAsync(userId, context, ct);
        await tx.CommitAsync(ct);
        return response;
```

換成：

```csharp
        db.XpEvents.AddRange(ProgressUpdater.Recalculate(context.Player, context.TodayLog, context.ActiveQuests, now));
        await db.SaveChangesAsync(ct);
        var rewards = await rewardApplier.ApplyAsync(context, 0, ct);
        var response = await BuildListAsync(userId, context, ct);
        await tx.CommitAsync(ct);
        return response with { Rewards = rewards };
```

3. `ArchiveAsync` 簽章

```csharp
    /// <returns>非同步作業。</returns>
    /// <exception cref="ApiErrorException">目標不存在或已封存（404）。</exception>
    public async Task ArchiveAsync(Guid userId, Guid goalId, CancellationToken ct)
```

換成：

```csharp
    /// <returns>本次獎勵（封存可能讓今日達標）。</returns>
    /// <exception cref="ApiErrorException">目標不存在或已封存（404）。</exception>
    public async Task<RewardsDto> ArchiveAsync(Guid userId, Guid goalId, CancellationToken ct)
```

4. `ArchiveAsync` 結尾

```csharp
        db.XpEvents.AddRange(ProgressUpdater.Recalculate(context.Player, context.TodayLog, context.ActiveQuests, now));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
```

換成：

```csharp
        db.XpEvents.AddRange(ProgressUpdater.Recalculate(context.Player, context.TodayLog, context.ActiveQuests, now));
        await db.SaveChangesAsync(ct);
        var rewards = await rewardApplier.ApplyAsync(context, 0, ct);
        await tx.CommitAsync(ct);
        return rewards;
    }
```

用 Edit 把 `src/SoloLeveling.Api/Controllers/GoalsController.cs` 的

```csharp
    /// <returns>204。</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await goals.ArchiveAsync(User.GetUserId(), id, ct);
        return NoContent();
    }
```

換成：

```csharp
    /// <returns>200 與本次獎勵。</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType<RewardsEnvelope>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RewardsEnvelope>> Archive(Guid id, CancellationToken ct)
    {
        return new RewardsEnvelope(await goals.ArchiveAsync(User.GetUserId(), id, ct));
    }
```

- [ ] **Step 10: 接到玩家與週期服務**

用 Edit 把 `src/SoloLeveling.Api/Services/PlayerService.cs` 從 `using Microsoft.EntityFrameworkCore;` 到檔尾的整份內容換成：

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Domain.Rules;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Services;

/// <summary>
/// 玩家總覽與設定。
/// </summary>
/// <param name="db">DbContext。</param>
/// <param name="loader">今日內容載入（含結算）。</param>
/// <param name="rewardApplier">獎勵判定與持久化。</param>
/// <param name="clock">時間來源。</param>
public class PlayerService(AppDbContext db, TodayContextLoader loader, RewardApplier rewardApplier, TimeProvider clock)
{
    /// <summary>
    /// GET /me：先結算、套用獎勵，再回傳總覽。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>總覽。</returns>
    public async Task<MeResponse> GetMeAsync(Guid userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        return await SaveAndBuildAsync(context, tx, ct);
    }

    /// <summary>
    /// PATCH /me：更新顯示名稱、時區、困難模式；困難模式變更會立即重算今日達標與獎勵。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="request">要更新的欄位。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>更新後的總覽。</returns>
    /// <exception cref="Errors.ApiErrorException">顯示名稱或時區不合法（400）。</exception>
    public async Task<MeResponse> PatchMeAsync(Guid userId, PatchMeRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);

        if (request.DisplayName is { } displayName)
        {
            var trimmed = displayName.Trim();
            if (trimmed.Length is 0 or > 40)
            {
                throw Errors.ApiErrorException.BadRequest("InvalidDisplayName", "顯示名稱需為 1–40 字");
            }

            context.User.DisplayName = trimmed;
        }

        if (request.TimeZoneId is { } timeZoneId)
        {
            context.User.TimeZoneId = AccountService.ValidateTimeZone(timeZoneId);
        }

        if (request.HardMode is { } hardMode && hardMode != context.Player.HardMode)
        {
            context.Player.HardMode = hardMode;
            var events = ProgressUpdater.Recalculate(context.Player, context.TodayLog, context.ActiveQuests, clock.GetUtcNow());
            db.XpEvents.AddRange(events);
        }

        return await SaveAndBuildAsync(context, tx, ct);
    }

    /// <summary>
    /// 存檔、套用獎勵、組回應並 commit；所有回傳 <see cref="MeResponse"/> 的端點共用。
    /// </summary>
    /// <param name="context">今日內容（已套用本次修改）。</param>
    /// <param name="tx">呼叫端開的交易。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>總覽（含本次獎勵）。</returns>
    private async Task<MeResponse> SaveAndBuildAsync(TodayContext context, IDbContextTransaction tx, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        var rewards = await rewardApplier.ApplyAsync(context, 0, ct);
        var program = await db.Programs.AsNoTracking().SingleAsync(p => p.UserId == context.User.Id && p.IsActive, ct);
        await tx.CommitAsync(ct);
        return new MeResponse(
            context.User.ToDto(),
            context.Player.ToDto(context.TodayLog.IsCleared),
            program.ToDto(context.Today),
            context.ActiveQuests.Count == 0,
            rewards);
    }
}
```

用 Edit 把 `src/SoloLeveling.Api/Services/ProgramService.cs` 從 `/// <summary>\n/// 66 天計畫週期。` 到檔尾換成：

```csharp
/// <summary>
/// 66 天計畫週期。
/// </summary>
/// <param name="db">DbContext。</param>
/// <param name="loader">今日內容載入（含結算）。</param>
/// <param name="rewardApplier">獎勵判定與持久化。</param>
/// <param name="clock">時間來源。</param>
public class ProgramService(AppDbContext db, TodayContextLoader loader, RewardApplier rewardApplier, TimeProvider clock)
{
    /// <summary>
    /// POST /program/restart：結束目前週期，從今日開新週期（Cycle + 1）；不重置玩家等級與屬性。
    /// 舊週期已滿 LengthDays 天時寫入 <c>CompletedAt</c> 並發 S 級寶箱。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>新週期（含本次獎勵）。</returns>
    public async Task<ProgramDto> RestartAsync(Guid userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        var current = await db.Programs.SingleAsync(p => p.UserId == userId && p.IsActive, ct);
        var now = clock.GetUtcNow();

        // 與 ProgramDto.IsCompleted 同一判定：今日已是第 LengthDays + 1 天以後
        var completedPrograms = 0;
        if (context.Today.DayNumber - current.StartDate.DayNumber >= current.LengthDays)
        {
            current.CompletedAt = now;
            completedPrograms = 1;
        }

        // 先關舊的再開新的，分兩次 SaveChanges 以免撞到「每人只能一筆 IsActive」的 partial unique index
        current.IsActive = false;
        await db.SaveChangesAsync(ct);

        var next = new ProgramEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StartDate = context.Today,
            Cycle = current.Cycle + 1,
            LengthDays = ProgramEntity.DefaultLengthDays,
            IsActive = true,
            CreatedAt = now,
        };
        db.Programs.Add(next);
        await db.SaveChangesAsync(ct);
        var rewards = await rewardApplier.ApplyAsync(context, completedPrograms, ct);
        await tx.CommitAsync(ct);
        return next.ToDto(context.Today) with { Rewards = rewards };
    }
}
```

- [ ] **Step 11: 跑測試確認通過**

Run: `dotnet test tests/SoloLeveling.Api.Tests --filter "FullyQualifiedName~RewardsFlowApiTests"`
Expected: 11 個 PASS。若 `螢幕時間…` 失敗，先確認 `RewardStatsLoader` 的日期範圍是 `>= today − 30 && <= today`，以及 `TodayService.SetProgressAsync` 是在 `SaveChangesAsync` **之後**才呼叫 `ApplyAsync`。

Run: `dotnet test`
Expected: 全部 PASS（`AccountApiTests` 的 `title == "新手"` 仍成立，因為沒選字塊時沿用階級稱號；三處封存測試已改為 200）。

Run: `dotnet format --verify-no-changes`
Expected: 無差異。

- [ ] **Step 12: Commit**

```bash
git add src/SoloLeveling.Api tests/SoloLeveling.Api.Tests
git commit -F - <<'EOF'
feat: 會改狀態的請求統一判定並回傳獎勵

1. 存檔後才查統計，判定才看得到本次進度與新建的目標，結果以 rewards 欄位回傳
2. 封存改回 200 帶 rewards，封存造成的達標金幣與升級訊息不會遺失

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NxkXCvcUbg2y9cvwZjMor4
EOF
```

---

### Task 7: 獎勵端點：總覽、開箱、圖鑑、商店、稱號／釘選卡／主題設定

**Files:**
- Modify: `src/SoloLeveling.Api/Contracts/RewardDtos.cs`（檔尾新增請求與回應 record）
- Create: `src/SoloLeveling.Api/Services/RewardService.cs`
- Create: `src/SoloLeveling.Api/Controllers/RewardsController.cs`
- Modify: `src/SoloLeveling.Api/Services/PlayerService.cs`（新增 3 個方法）
- Modify: `src/SoloLeveling.Api/Controllers/MeController.cs`（新增 3 個 PUT）
- Modify: `src/SoloLeveling.Api/Program.cs`（`RewardService`、`Random`）
- Test: Create `tests/SoloLeveling.Api.Tests/FixedRandom.cs`、`tests/SoloLeveling.Api.Tests/RewardsApiTests.cs`；Modify `tests/SoloLeveling.Api.Tests/ApiFactory.cs`

**Interfaces:**
- Consumes: Task 6 的 `RewardApplier.ApplyAsync`、`RewardStatsLoader.LoadAsync`、`ChestDto`、`CardDto`、`RewardsDto`、mapper；Task 1–2 的目錄、`Loot.Open`、`Wallet`、`Shop`、`Themes`。
- Produces:
  - `record AchievementDto(string Key, string Name, string Condition, string TitleText, TitleSlot Slot, bool Unlocked, int Progress, int Target, long? UnlockedAt)`
  - `record TitleFragmentDto(string Key, string Text, TitleSlot Slot)`
  - `record RewardsResponse(int Coins, int ShieldCount, List<ChestDto> UnopenedChests, List<AchievementDto> Achievements, List<TitleFragmentDto> TitleFragments, string? TitlePrefixKey, string? TitleSuffixKey, string Title, CardDto? PinnedCard, string ThemeKey, List<string> OwnedThemes)`
  - `record OpenChestResponse(CardDto Card, bool IsDuplicate, int Coins, int CoinBalance, RewardsDto Rewards)`
  - `record CardEntryDto(string Id, string Name, Rarity Rarity, string Flavor, string Image, bool Owned, int Count, long? FirstAcquiredAt)`、`record CardsResponse(List<CardEntryDto> Cards, int OwnedKinds, int Total)`
  - `record SetTitleRequest(string? PrefixKey, string? SuffixKey)`、`record SetPinnedCardRequest(string? CardId)`、`record SetThemeRequest(string ThemeKey)`
  - `record PurchaseRequest(ShopItem Item, string? ThemeKey)`、`record PurchaseResponse(int Coins, int ShieldCount, List<string> OwnedThemes, ChestDto? Chest, RewardsDto Rewards)`
  - 端點：`GET /api/v1/rewards`、`POST /api/v1/rewards/chests/{id}/open`、`GET /api/v1/cards`、`POST /api/v1/shop/purchase`、`PUT /api/v1/me/title`、`PUT /api/v1/me/pinned-card`、`PUT /api/v1/me/theme`
  - 測試用：`FixedRandom : Random`（`int Value`），`ApiFactory.Rng`

- [ ] **Step 1: 寫失敗的測試**

建立 `tests/SoloLeveling.Api.Tests/FixedRandom.cs`：

```csharp
namespace SoloLeveling.Api.Tests;

/// <summary>
/// 固定回傳 <see cref="Value"/>（超過上限時取最後一個索引），讓抽卡結果可預期；預設 0＝該等級目錄中的第一張卡。
/// </summary>
public sealed class FixedRandom : Random
{
    public int Value { get; set; }

    public override int Next(int maxValue)
    {
        return Math.Min(Value, maxValue - 1);
    }
}
```

用 Edit 修改 `tests/SoloLeveling.Api.Tests/ApiFactory.cs`：

把

```csharp
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));
```

換成：

```csharp
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));

    /// <summary>抽卡亂數；預設永遠抽該等級的第一張卡，測試可改 <see cref="FixedRandom.Value"/>。</summary>
    public FixedRandom Rng { get; } = new();
```

把

```csharp
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
```

換成：

```csharp
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<Random>();
            services.AddSingleton<Random>(Rng);
```

建立 `tests/SoloLeveling.Api.Tests/RewardsApiTests.cs`：

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Entities;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class RewardsApiTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory _factory = new(fixture.ConnectionString);

    public void Dispose()
    {
        _factory.Dispose();
    }

    private async Task<(HttpClient Client, Guid UserId)> RegisterAsync()
    {
        var client = await _factory.RegisterAsync();
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        return (client, me.GetProperty("user").GetProperty("id").GetGuid());
    }

    private async Task SeedAsync(Action<AppDbContext> seed)
    {
        await using var db = fixture.CreateDbContext();
        seed(db);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedChestAsync(Guid userId, Rarity rarity)
    {
        var id = Guid.NewGuid();
        await SeedAsync(db => db.RewardChests.Add(new RewardChest { Id = id, UserId = userId, Rarity = rarity, Source = ChestSource.Purchase, CreatedAt = _factory.Clock.GetUtcNow() }));
        return id;
    }

    /// <summary>測試直接種金幣餘額（不經 Wallet、不寫事件），只用來準備商店情境。</summary>
    private async Task SetCoinsAsync(Guid userId, int coins)
    {
        await using var db = fixture.CreateDbContext();
        var player = await db.Players.SingleAsync(p => p.UserId == userId);
        player.Coins = coins;
        await db.SaveChangesAsync();
    }

    private static Task<HttpResponseMessage> OpenAsync(HttpClient client, Guid chestId)
    {
        return client.PostAsync($"/api/v1/rewards/chests/{chestId}/open", null);
    }

    private static Task<HttpResponseMessage> BuyAsync(HttpClient client, string item, string? themeKey = null)
    {
        return client.PostAsJsonAsync("/api/v1/shop/purchase", new { item, themeKey });
    }

    private static async Task<JsonElement> OkJsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task ShouldFailAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be(code);
    }

    [Fact]
    public async Task 開箱_新卡加入收藏_再抽到同卡轉成重複金幣()
    {
        var (client, userId) = await RegisterAsync();
        var first = await SeedChestAsync(userId, Rarity.E);
        var second = await SeedChestAsync(userId, Rarity.E);
        _factory.Rng.Value = 0;

        var r1 = await OkJsonAsync(await OpenAsync(client, first));
        r1.GetProperty("card").GetProperty("id").GetString().Should().Be("rusty-dagger");
        r1.GetProperty("card").GetProperty("image").GetString().Should().Be("cards/rusty-dagger.webp");
        r1.GetProperty("isDuplicate").GetBoolean().Should().BeFalse();
        r1.GetProperty("coins").GetInt32().Should().Be(20);
        r1.GetProperty("coinBalance").GetInt32().Should().Be(20);

        var r2 = await OkJsonAsync(await OpenAsync(client, second));
        r2.GetProperty("isDuplicate").GetBoolean().Should().BeTrue();
        r2.GetProperty("coins").GetInt32().Should().Be(50);
        r2.GetProperty("coinBalance").GetInt32().Should().Be(70);

        var cards = await client.GetFromJsonAsync<JsonElement>("/api/v1/cards");
        cards.GetProperty("total").GetInt32().Should().Be(25);
        cards.GetProperty("ownedKinds").GetInt32().Should().Be(1);
        var dagger = cards.GetProperty("cards").EnumerateArray().Single(c => c.GetProperty("id").GetString() == "rusty-dagger");
        dagger.GetProperty("owned").GetBoolean().Should().BeTrue();
        dagger.GetProperty("count").GetInt32().Should().Be(2);
        dagger.GetProperty("firstAcquiredAt").GetInt64().Should().Be(_factory.Clock.GetUtcNow().ToUnixTimeSeconds());

        var rewards = await client.GetFromJsonAsync<JsonElement>("/api/v1/rewards");
        rewards.GetProperty("unopenedChests").GetArrayLength().Should().Be(0);
        rewards.GetProperty("coins").GetInt32().Should().Be(70);
        await using var db = fixture.CreateDbContext();
        var events = await db.CoinEvents.Where(e => e.UserId == userId).ToListAsync();
        events.Sum(e => e.Amount).Should().Be(70);
        events.Count(e => e.Source == CoinSource.ChestOpen).Should().Be(2);
        events.Should().ContainSingle(e => e.Source == CoinSource.DuplicateCard && e.Amount == 30 && e.RefId == second);
    }

    [Fact]
    public async Task 開已開過或別人的寶箱_回404()
    {
        var (client, userId) = await RegisterAsync();
        var other = await _factory.RegisterAsync();
        var chest = await SeedChestAsync(userId, Rarity.C);

        await ShouldFailAsync(await OpenAsync(other, chest), HttpStatusCode.NotFound, "ChestNotFound");
        await OkJsonAsync(await OpenAsync(client, chest));
        await ShouldFailAsync(await OpenAsync(client, chest), HttpStatusCode.NotFound, "ChestNotFound");
        await ShouldFailAsync(await OpenAsync(client, Guid.NewGuid()), HttpStatusCode.NotFound, "ChestNotFound");

        await using var db = fixture.CreateDbContext();
        (await db.OwnedCards.Where(c => c.UserId == userId).SumAsync(c => c.Count)).Should().Be(1);
        (await db.Players.SingleAsync(p => p.UserId == userId)).Coins.Should().Be(50);
    }

    [Fact]
    public async Task 開箱湊滿10種卡_解鎖收藏家()
    {
        var (client, userId) = await RegisterAsync();
        var now = _factory.Clock.GetUtcNow();
        await SeedAsync(db => db.OwnedCards.AddRange(Cards.OfRarity(Rarity.E).Skip(1).Take(9)
            .Select(c => new OwnedCard { UserId = userId, CardId = c.Id, Count = 1, FirstAcquiredAt = now })));
        var chest = await SeedChestAsync(userId, Rarity.E);
        _factory.Rng.Value = 0;

        var body = await OkJsonAsync(await OpenAsync(client, chest));

        body.GetProperty("isDuplicate").GetBoolean().Should().BeFalse();
        body.GetProperty("rewards").GetProperty("newAchievements").EnumerateArray()
            .Select(a => a.GetProperty("key").GetString()).Should().Contain("collector-10");
        body.GetProperty("coinBalance").GetInt32().Should().Be(70);
    }

    [Fact]
    public async Task 商店_扣款與各錯誤碼()
    {
        var (client, userId) = await RegisterAsync();
        await SetCoinsAsync(userId, 50);
        await ShouldFailAsync(await BuyAsync(client, "Shield"), HttpStatusCode.BadRequest, "NotEnoughCoins");

        await SetCoinsAsync(userId, 1000);
        JsonElement last = default;
        for (var i = 0; i < 3; i++)
        {
            last = await OkJsonAsync(await BuyAsync(client, "Shield"));
        }

        last.GetProperty("shieldCount").GetInt32().Should().Be(3);
        last.GetProperty("coins").GetInt32().Should().Be(700);
        await ShouldFailAsync(await BuyAsync(client, "Shield"), HttpStatusCode.BadRequest, "ShieldLimitReached");

        var violet = await OkJsonAsync(await BuyAsync(client, "Theme", "violet"));
        violet.GetProperty("coins").GetInt32().Should().Be(200);
        violet.GetProperty("ownedThemes").EnumerateArray().Select(t => t.GetString()).Should().Equal("azure", "violet");
        await ShouldFailAsync(await BuyAsync(client, "Theme", "violet"), HttpStatusCode.BadRequest, "ThemeOwned");
        await ShouldFailAsync(await BuyAsync(client, "Theme", "azure"), HttpStatusCode.BadRequest, "ThemeOwned");
        await ShouldFailAsync(await BuyAsync(client, "Theme", "neon"), HttpStatusCode.BadRequest, "UnknownTheme");

        var chest = await OkJsonAsync(await BuyAsync(client, "EChest"));
        chest.GetProperty("coins").GetInt32().Should().Be(0);
        chest.GetProperty("chest").GetProperty("rarity").GetString().Should().Be("E");
        chest.GetProperty("chest").GetProperty("source").GetString().Should().Be("Purchase");
        await ShouldFailAsync(await BuyAsync(client, "Theme", "jade"), HttpStatusCode.BadRequest, "NotEnoughCoins");

        var rewards = await client.GetFromJsonAsync<JsonElement>("/api/v1/rewards");
        rewards.GetProperty("unopenedChests").EnumerateArray().Select(c => c.GetProperty("id").GetGuid())
            .Should().Equal(chest.GetProperty("chest").GetProperty("id").GetGuid());
        await using var db = fixture.CreateDbContext();
        (await db.CoinEvents.Where(e => e.UserId == userId).SumAsync(e => e.Amount)).Should().Be(-1000);
        (await db.Players.SingleAsync(p => p.UserId == userId)).Coins.Should().Be(0);
    }

    [Fact]
    public async Task 稱號組合_只能選已解鎖且槽位正確的字塊()
    {
        var (client, userId) = await RegisterAsync();
        var now = _factory.Clock.GetUtcNow();
        await SeedAsync(db => db.Achievements.AddRange(
            new Achievement { UserId = userId, Key = "bed-30", UnlockedAt = now },
            new Achievement { UserId = userId, Key = "quests-100", UnlockedAt = now }));

        var both = await OkJsonAsync(await client.PutAsJsonAsync("/api/v1/me/title", new { prefixKey = "bed-30", suffixKey = "quests-100" }));
        both.GetProperty("player").GetProperty("title").GetString().Should().Be("靜夜的・百戰獵人");
        both.GetProperty("player").GetProperty("rankTitle").GetString().Should().Be("新手");

        await ShouldFailAsync(await client.PutAsJsonAsync("/api/v1/me/title", new { prefixKey = "quests-100", suffixKey = (string?)null }), HttpStatusCode.BadRequest, "TitleNotUnlocked");
        await ShouldFailAsync(await client.PutAsJsonAsync("/api/v1/me/title", new { prefixKey = (string?)null, suffixKey = "collector-10" }), HttpStatusCode.BadRequest, "TitleNotUnlocked");

        var prefixOnly = await OkJsonAsync(await client.PutAsJsonAsync("/api/v1/me/title", new { prefixKey = "bed-30", suffixKey = (string?)null }));
        prefixOnly.GetProperty("player").GetProperty("title").GetString().Should().Be("靜夜的");
        var none = await OkJsonAsync(await client.PutAsJsonAsync("/api/v1/me/title", new { prefixKey = (string?)null, suffixKey = (string?)null }));
        none.GetProperty("player").GetProperty("title").GetString().Should().Be("新手");

        var rewards = await client.GetFromJsonAsync<JsonElement>("/api/v1/rewards");
        rewards.GetProperty("titleFragments").EnumerateArray().Select(f => f.GetProperty("key").GetString()).Should().Equal("quests-100", "bed-30");
        rewards.GetProperty("title").GetString().Should().Be("新手");
    }

    [Fact]
    public async Task 主題切換_未擁有回400_擁有後可切換()
    {
        var (client, userId) = await RegisterAsync();

        await ShouldFailAsync(await client.PutAsJsonAsync("/api/v1/me/theme", new { themeKey = "violet" }), HttpStatusCode.BadRequest, "ThemeNotOwned");
        await SeedAsync(db => db.OwnedThemes.Add(new OwnedTheme { UserId = userId, ThemeKey = "violet" }));

        var violet = await OkJsonAsync(await client.PutAsJsonAsync("/api/v1/me/theme", new { themeKey = "violet" }));
        violet.GetProperty("player").GetProperty("themeKey").GetString().Should().Be("violet");
        var azure = await OkJsonAsync(await client.PutAsJsonAsync("/api/v1/me/theme", new { themeKey = "azure" }));
        azure.GetProperty("player").GetProperty("themeKey").GetString().Should().Be("azure");
        await ShouldFailAsync(await client.PutAsJsonAsync("/api/v1/me/theme", new { themeKey = "neon" }), HttpStatusCode.BadRequest, "ThemeNotOwned");
    }

    [Fact]
    public async Task 釘選卡片_未擁有回400_擁有後顯示在me_可取消()
    {
        var (client, userId) = await RegisterAsync();

        await ShouldFailAsync(await client.PutAsJsonAsync("/api/v1/me/pinned-card", new { cardId = "arise" }), HttpStatusCode.BadRequest, "CardNotOwned");
        await SeedAsync(db => db.OwnedCards.Add(new OwnedCard { UserId = userId, CardId = "arise", Count = 1, FirstAcquiredAt = _factory.Clock.GetUtcNow() }));

        var pinned = await OkJsonAsync(await client.PutAsJsonAsync("/api/v1/me/pinned-card", new { cardId = "arise" }));
        var card = pinned.GetProperty("player").GetProperty("pinnedCard");
        card.GetProperty("id").GetString().Should().Be("arise");
        card.GetProperty("rarity").GetString().Should().Be("S");

        var cleared = await OkJsonAsync(await client.PutAsJsonAsync("/api/v1/me/pinned-card", new { cardId = (string?)null }));
        cleared.GetProperty("player").GetProperty("pinnedCard").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetRewards_列出12個成就與進度_新帳號皆未解鎖()
    {
        var (client, _) = await RegisterAsync();
        var quests = await client.GetFromJsonAsync<JsonElement>("/api/v1/quests");
        await client.PutAsJsonAsync($"/api/v1/today/quests/{quests[0].GetProperty("id").GetGuid()}/progress", new { value = 1 });

        var rewards = await client.GetFromJsonAsync<JsonElement>("/api/v1/rewards");

        var achievements = rewards.GetProperty("achievements").EnumerateArray().ToList();
        achievements.Should().HaveCount(12);
        achievements.Should().OnlyContain(a => !a.GetProperty("unlocked").GetBoolean());
        var hundred = achievements.Single(a => a.GetProperty("key").GetString() == "quests-100");
        hundred.GetProperty("progress").GetInt32().Should().Be(1);
        hundred.GetProperty("target").GetInt32().Should().Be(100);
        hundred.GetProperty("unlockedAt").ValueKind.Should().Be(JsonValueKind.Null);
        rewards.GetProperty("coins").GetInt32().Should().Be(0);
        rewards.GetProperty("themeKey").GetString().Should().Be("azure");
        rewards.GetProperty("ownedThemes").EnumerateArray().Select(t => t.GetString()).Should().Equal("azure");
        rewards.GetProperty("title").GetString().Should().Be("新手");
        rewards.GetProperty("pinnedCard").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/SoloLeveling.Api.Tests --filter "FullyQualifiedName~RewardsApiTests"`
Expected: 編譯通過；8 個 FAIL，狀態碼為 404（路由不存在）或 405。

- [ ] **Step 3: 請求與回應 DTO**

用 Edit 在 `src/SoloLeveling.Api/Contracts/RewardDtos.cs` 檔尾（`public record RewardsEnvelope(RewardsDto Rewards);` 之後）加入：

```csharp

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
```

- [ ] **Step 4: RewardService**

建立 `src/SoloLeveling.Api/Services/RewardService.cs`：

```csharp
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
```

- [ ] **Step 5: 稱號、釘選卡、主題設定**

用 Edit 修改 `src/SoloLeveling.Api/Services/PlayerService.cs`：

1. 檔首 `using SoloLeveling.Api.Contracts;` 之後加入：

```csharp
using SoloLeveling.Api.Errors;
using SoloLeveling.Domain;
```

2. 在

```csharp
    /// <summary>
    /// 存檔、套用獎勵、組回應並 commit；所有回傳 <see cref="MeResponse"/> 的端點共用。
```

之前加入：

```csharp
    /// <summary>
    /// PUT /me/title：設定稱號前綴與後綴；可只選一邊或都不選。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="request">前綴與後綴字塊鍵。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>更新後的總覽。</returns>
    /// <exception cref="ApiErrorException">字塊未解鎖或槽位不符（400，<c>TitleNotUnlocked</c>）。</exception>
    public async Task<MeResponse> SetTitleAsync(Guid userId, SetTitleRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        var unlocked = await db.Achievements.Where(a => a.UserId == userId).Select(a => a.Key).ToListAsync(ct);
        EnsureFragment(request.PrefixKey, TitleSlot.Prefix, unlocked);
        EnsureFragment(request.SuffixKey, TitleSlot.Suffix, unlocked);
        context.Player.TitlePrefixKey = request.PrefixKey;
        context.Player.TitleSuffixKey = request.SuffixKey;
        return await SaveAndBuildAsync(context, tx, ct);
    }

    /// <summary>
    /// PUT /me/pinned-card：從已擁有的卡中釘選一張，傳 null 取消。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="request">卡片 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>更新後的總覽。</returns>
    /// <exception cref="ApiErrorException">尚未擁有或不存在的卡（400，<c>CardNotOwned</c>）。</exception>
    public async Task<MeResponse> SetPinnedCardAsync(Guid userId, SetPinnedCardRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        if (request.CardId is { } cardId && !await db.OwnedCards.AnyAsync(c => c.UserId == userId && c.CardId == cardId, ct))
        {
            throw ApiErrorException.BadRequest("CardNotOwned", "尚未擁有這張卡片");
        }

        context.Player.PinnedCardId = request.CardId;
        return await SaveAndBuildAsync(context, tx, ct);
    }

    /// <summary>
    /// PUT /me/theme：切換到已擁有的主題（azure 永遠可用）。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="request">主題鍵。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>更新後的總覽。</returns>
    /// <exception cref="ApiErrorException">尚未擁有或不存在的主題（400，<c>ThemeNotOwned</c>）。</exception>
    public async Task<MeResponse> SetThemeAsync(Guid userId, SetThemeRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        var key = request.ThemeKey;
        var owned = key == Themes.Default || await db.OwnedThemes.AnyAsync(t => t.UserId == userId && t.ThemeKey == key, ct);
        if (!owned)
        {
            throw ApiErrorException.BadRequest("ThemeNotOwned", "尚未擁有此主題");
        }

        context.Player.ThemeKey = key;
        return await SaveAndBuildAsync(context, tx, ct);
    }

    /// <summary>
    /// 字塊必須已解鎖且位置正確；null 表示不選，直接通過。
    /// </summary>
    /// <param name="key">字塊鍵。</param>
    /// <param name="slot">要放的位置。</param>
    /// <param name="unlocked">已解鎖的成就鍵。</param>
    /// <exception cref="ApiErrorException">未解鎖或位置不符（400，<c>TitleNotUnlocked</c>）。</exception>
    private static void EnsureFragment(string? key, TitleSlot slot, List<string> unlocked)
    {
        if (key is null)
        {
            return;
        }

        if (!unlocked.Contains(key) || Achievements.Find(key)?.Slot != slot)
        {
            throw ApiErrorException.BadRequest("TitleNotUnlocked", "稱號字塊尚未解鎖或位置不符");
        }
    }

```

3. 因為加了 `using SoloLeveling.Api.Errors;`，把 `PatchMeAsync` 內的 `Errors.ApiErrorException.BadRequest(` 改成 `ApiErrorException.BadRequest(`，`<exception cref="Errors.ApiErrorException">` 改成 `<exception cref="ApiErrorException">`。

- [ ] **Step 6: Controller 與 DI**

建立 `src/SoloLeveling.Api/Controllers/RewardsController.cs`：

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoloLeveling.Api.Auth;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Services;

namespace SoloLeveling.Api.Controllers;

/// <summary>
/// 獎勵總覽、開箱、圖鑑與商店。
/// </summary>
/// <param name="rewards">獎勵服務。</param>
[ApiController]
[Authorize]
[Route("api/v1")]
public class RewardsController(RewardService rewards) : ControllerBase
{
    /// <summary>
    /// 金幣、保險卡、未開寶箱、成就與進度、稱號字塊、釘選卡與主題。
    /// </summary>
    /// <param name="ct">取消權杖。</param>
    /// <returns>總覽。</returns>
    [HttpGet("rewards")]
    [ProducesResponseType<RewardsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RewardsResponse>> Get(CancellationToken ct)
    {
        return await rewards.GetAsync(User.GetUserId(), ct);
    }

    /// <summary>
    /// 開啟一個未開寶箱。
    /// </summary>
    /// <param name="id">寶箱 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>掉落卡片、是否重複、金幣與餘額。</returns>
    [HttpPost("rewards/chests/{id:guid}/open")]
    [ProducesResponseType<OpenChestResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OpenChestResponse>> OpenChest(Guid id, CancellationToken ct)
    {
        return await rewards.OpenChestAsync(User.GetUserId(), id, ct);
    }

    /// <summary>
    /// 卡片目錄與擁有狀態。
    /// </summary>
    /// <param name="ct">取消權杖。</param>
    /// <returns>圖鑑。</returns>
    [HttpGet("cards")]
    [ProducesResponseType<CardsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CardsResponse>> GetCards(CancellationToken ct)
    {
        return await rewards.GetCardsAsync(User.GetUserId(), ct);
    }

    /// <summary>
    /// 購買連勝保險卡、E 級寶箱或主題。
    /// </summary>
    /// <param name="request">商品與主題鍵。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>購買結果。</returns>
    [HttpPost("shop/purchase")]
    [ProducesResponseType<PurchaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PurchaseResponse>> Purchase(PurchaseRequest request, CancellationToken ct)
    {
        return await rewards.PurchaseAsync(User.GetUserId(), request, ct);
    }
}
```

用 Edit 在 `src/SoloLeveling.Api/Controllers/MeController.cs` 類別最後一個 `}` 之前加入：

```csharp

    /// <summary>
    /// 設定稱號組合；只能選已解鎖且槽位正確的字塊，可只選一邊或都不選。
    /// </summary>
    /// <param name="request">前綴與後綴字塊鍵，null 表示不選。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>更新後的總覽。</returns>
    [HttpPut("title")]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MeResponse>> SetTitle(SetTitleRequest request, CancellationToken ct)
    {
        return await players.SetTitleAsync(User.GetUserId(), request, ct);
    }

    /// <summary>
    /// 釘選一張已擁有的卡片；cardId 為 null 時取消。
    /// </summary>
    /// <param name="request">卡片 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>更新後的總覽。</returns>
    [HttpPut("pinned-card")]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MeResponse>> SetPinnedCard(SetPinnedCardRequest request, CancellationToken ct)
    {
        return await players.SetPinnedCardAsync(User.GetUserId(), request, ct);
    }

    /// <summary>
    /// 切換到已擁有的主題。
    /// </summary>
    /// <param name="request">主題鍵。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>更新後的總覽。</returns>
    [HttpPut("theme")]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MeResponse>> SetTheme(SetThemeRequest request, CancellationToken ct)
    {
        return await players.SetThemeAsync(User.GetUserId(), request, ct);
    }
```

用 Edit 修改 `src/SoloLeveling.Api/Program.cs`：

把

```csharp
builder.Services.AddSingleton(TimeProvider.System);
```

換成：

```csharp
builder.Services.AddSingleton(TimeProvider.System);
// 抽卡亂數；Random.Shared 執行緒安全，測試以固定序列取代
builder.Services.AddSingleton<Random>(Random.Shared);
```

在 `builder.Services.AddScoped<GoalService>();` 之後加入：

```csharp
builder.Services.AddScoped<RewardService>();
```

- [ ] **Step 7: 跑測試確認通過**

Run: `dotnet test tests/SoloLeveling.Api.Tests --filter "FullyQualifiedName~RewardsApiTests"`
Expected: 8 個 PASS。

Run: `dotnet test`
Expected: 全部 PASS。

Run: `dotnet format --verify-no-changes`
Expected: 無差異。

- [ ] **Step 8: Commit**

```bash
git add src/SoloLeveling.Api tests/SoloLeveling.Api.Tests
git commit -F - <<'EOF'
feat: 新增獎勵總覽、開箱、圖鑑、商店與稱號主題設定端點

1. 開箱與購買沿用結算列鎖，重複開箱與併發請求不會重複發卡或扣款
2. 稱號、釘選卡、主題只能設成已解鎖或已擁有的項目，錯誤碼依規格

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NxkXCvcUbg2y9cvwZjMor4
EOF
```

---

### Task 8: 前端：檔案 HUNTER 頁、商店、主題設定、獎勵系統訊息

本 task 修改 UI 改版計畫產出的 `ui.js`／`app.js`；只依賴前端介面約定（`.superpowers/sdd/plan-drafting/frontend-contract.md`）列出的名稱：`window.UI`（`h`、`sysMessage`、`toastError`、`win`、`statusPanel`、`confirmButton`、`setAccent`）、`NAV_ITEMS`、`route()`、`announce(prevMe, prevToday, me, today, rewards?)`，以及沿用至今的 `api(method, path, body)`、`state`、`loadToday()`、`renderSettings()`、`$view`。Step 1 先確認這些名稱存在。

**Files:**
- Create: `src/SoloLeveling.Api/wwwroot/hunter.js`
- Modify: `src/SoloLeveling.Api/wwwroot/index.html`（載入 `hunter.js`）
- Modify: `src/SoloLeveling.Api/wwwroot/app.js`（導覽、`api()` 收集 rewards、`announce()`、`route()`、設定頁主題區塊）
- Modify: `src/SoloLeveling.Api/wwwroot/ui.js`（狀態面板名稱旁的釘選卡小圖）
- Modify: `src/SoloLeveling.Api/wwwroot/app.css`（檔案頁樣式）

**Interfaces:**
- Consumes: Task 6–7 的 API：`rewards` 欄位（`levelsGained`、`rankUps`、`newChests`、`newAchievements`、`shieldsGained`、`shieldsUsed`、`shieldCount`）、`GET /rewards`、`GET /cards`、`POST /rewards/chests/{id}/open`、`POST /shop/purchase`、`PUT /me/title|pinned-card|theme`、`me.player.coins／shieldCount／title／themeKey／pinnedCard`。
- Produces:
  - `window.Hunter.render({ api, view, me, today, reload }) → Promise<void>`：畫檔案頁
  - `window.Hunter.themeSection({ api, reload }) → Promise<{ html: string, bind(root: Element): void }>`：設定頁的主題視窗
  - `app.js`：`state.pendingRewards: RewardsDto[]`、`drainRewards() → RewardsDto[]`、`announceRewards(list, me)`

- [ ] **Step 1: 確認 UI 改版後的名稱**

Run: `grep -n "const NAV_ITEMS\|function announce\|async function api\|async function route\|async function loadToday\|async function renderSettings\|const state\|const \$view" src/SoloLeveling.Api/wwwroot/app.js && grep -n "setAccent\|statusPanel\|confirmButton\|sysMessage\|toastError\|window.UI" src/SoloLeveling.Api/wwwroot/ui.js`
Expected: 每個名稱都至少出現一次。若 UI 改版用了不同名稱（例如 `$view` 叫 `view`），以下步驟的程式碼一律改用實際名稱，並在 commit body 註明。

- [ ] **Step 2: 建立 `hunter.js`**

建立 `src/SoloLeveling.Api/wwwroot/hunter.js`：

```js
/* 檔案 HUNTER 畫面與設定頁的主題區塊。依賴 ui.js 的 window.UI；資料一律來自 API，前端不計算金幣、進度或等級。 */
(() => {
  const RARITY_ORDER = ['S', 'A', 'C', 'E'];
  const CHEST_SOURCE = {
    LevelUp: '升級',
    RankUp: '晉階',
    Streak7: '連續 7 天',
    Streak30: '連續 30 天',
    GoalCompleted: '目標完成',
    ProgramCompleted: '66 天完成',
    Purchase: '商店',
  };
  const THEMES = [
    { key: 'azure', name: '藍光', price: 0 },
    { key: 'violet', name: '紫影', price: 500 },
    { key: 'jade', name: '翡翠', price: 500 },
  ];
  const SHOP = [
    { item: 'Shield', name: '連勝保險卡', price: 100, note: '最多持有 3 張；漏一天時自動生效。' },
    { item: 'EChest', name: 'E 級寶箱', price: 200, note: '開出 E 級卡片與金幣。' },
  ];
  const h = (s) => UI.h(s);
  // API 時間戳是 Unix 秒；這裡是前端唯一轉成 Date 顯示的地方
  const fromUnixSeconds = (seconds) => new Date(seconds * 1000);

  // 圖檔不存在時 onerror 移除 img，留下稀有度色塊與名稱當佔位卡
  function cardFace(card, { owned = true, large = false } = {}) {
    const size = large ? ' hcard-lg' : '';
    if (!owned) {
      return `<div class="hcard hcard-unknown rarity-${card.rarity}${size}"><span class="hcard-q">？</span></div>`;
    }
    return `
      <div class="hcard rarity-${card.rarity}${size}">
        <img src="${h(card.image)}" alt="" loading="lazy" onerror="this.remove()">
        <div class="hcard-frame"><span class="hcard-rarity">${card.rarity}</span><span class="hcard-name">${h(card.name)}</span></div>
      </div>`;
  }

  function overlay(html) {
    const el = document.createElement('div');
    el.className = 'hunter-modal';
    el.innerHTML = `<div class="hunter-modal-inner">${html}</div>`;
    el.addEventListener('click', (e) => {
      if (e.target === el) {
        el.remove();
      }
    });
    document.body.appendChild(el);
    return el;
  }

  async function run(action) {
    try {
      await action();
    } catch (err) {
      UI.toastError(err.message);
    }
  }

  async function openChest(ctx, chestId) {
    await run(async () => {
      const result = await ctx.api('POST', `/rewards/chests/${chestId}/open`);
      const el = overlay(`
        <div class="flip"><div class="flip-inner">
          <div class="flip-back"><span>${result.card.rarity}</span></div>
          <div class="flip-front">${cardFace(result.card, { large: true })}</div>
        </div></div>
        <p class="flip-flavor">${h(result.card.flavor)}</p>
        <button class="btn btn-primary" id="flip-close">收下</button>`);
      requestAnimationFrame(() => el.querySelector('.flip').classList.add('flipped'));
      UI.sysMessage([
        `獲得 ${result.card.rarity} 級卡片「${result.card.name}」。`,
        result.isDuplicate ? `重複卡片已轉換。獲得 ${result.coins} 金幣。` : `新卡片已收入圖鑑。獲得 ${result.coins} 金幣。`,
      ]);
      el.querySelector('#flip-close').addEventListener('click', async () => {
        el.remove();
        await ctx.reload();
      });
    });
  }

  function showCard(ctx, card, pinnedId) {
    const pinned = card.id === pinnedId;
    const el = overlay(`
      ${cardFace(card, { large: true })}
      <div class="card-detail">
        <div class="card-detail-name">${card.rarity} 級・${h(card.name)}</div>
        <p>${h(card.flavor)}</p>
        <p class="muted">持有 ${card.count} 張・首次取得 ${fromUnixSeconds(card.firstAcquiredAt).toLocaleDateString('zh-TW')}</p>
        <button class="btn ${pinned ? 'btn-ghost' : 'btn-primary'}" id="pin">${pinned ? '取消釘選' : '釘選展示'}</button>
      </div>`);
    el.querySelector('#pin').addEventListener('click', () => run(async () => {
      await ctx.api('PUT', '/me/pinned-card', { cardId: pinned ? null : card.id });
      el.remove();
      await ctx.reload();
    }));
  }

  function openShop(ctx, rewards) {
    const el = overlay(UI.win({
      title: '商店',
      body: `
        <div class="shop-coins">金幣 <b>${rewards.coins}</b>・保險卡 <b>${rewards.shieldCount}</b> / 3</div>
        ${SHOP.map((s) => `
          <div class="shop-item">
            <div><div class="shop-name">${h(s.name)}</div><div class="muted">${h(s.note)}</div></div>
            <button class="btn btn-primary" data-item="${s.item}">${s.price} 金幣</button>
          </div>`).join('')}
        <div class="shop-item">
          <div><div class="shop-name">主題色</div><div class="muted">紫影、翡翠各 500 金幣。</div></div>
          <a class="btn btn-ghost" href="#settings">前往設定</a>
        </div>`,
    }));
    el.querySelectorAll('[data-item]').forEach((b) => b.addEventListener('click', () => run(async () => {
      const res = await ctx.api('POST', '/shop/purchase', { item: b.dataset.item });
      UI.sysMessage([
        b.dataset.item === 'Shield' ? `購買連勝保險卡。持有 ${res.shieldCount} 張。` : '購買 E 級寶箱，已放入待開寶箱。',
        `剩餘金幣 ${res.coins}。`,
      ]);
      el.remove();
      await ctx.reload();
    })));
    el.querySelector('a[href="#settings"]').addEventListener('click', () => el.remove());
  }

  async function render(ctx) {
    const [rewards, cards] = await Promise.all([ctx.api('GET', '/rewards'), ctx.api('GET', '/cards')]);
    const prefixes = rewards.titleFragments.filter((f) => f.slot === 'Prefix');
    const suffixes = rewards.titleFragments.filter((f) => f.slot === 'Suffix');
    const options = (list, selected) => ['<option value="">（不選）</option>']
      .concat(list.map((f) => `<option value="${h(f.key)}"${f.key === selected ? ' selected' : ''}>${h(f.text)}</option>`))
      .join('');
    const sorted = [...cards.cards].sort((a, b) => RARITY_ORDER.indexOf(a.rarity) - RARITY_ORDER.indexOf(b.rarity));

    ctx.view.innerHTML = `
      ${rewards.pinnedCard ? UI.win({ title: '展示卡', body: `<div class="hunter-pinned">${cardFace(rewards.pinnedCard, { large: true })}</div>` }) : ''}
      ${UI.statusPanel({ me: ctx.me, today: ctx.today, compact: false })}
      ${UI.win({
        title: '稱號',
        body: `
          <div class="title-now">${h(rewards.title)}</div>
          <div class="title-picker">
            <label>前綴<select id="title-prefix">${options(prefixes, rewards.titlePrefixKey)}</select></label>
            <label>後綴<select id="title-suffix">${options(suffixes, rewards.titleSuffixKey)}</select></label>
          </div>`,
      })}
      ${UI.win({
        title: '資源',
        body: `
          <div class="hunter-wallet">
            <span class="chip">金幣 <b>${rewards.coins}</b></span>
            <span class="chip">保險卡 <b>${rewards.shieldCount}</b> / 3</span>
            <button class="btn btn-ghost" id="open-shop">商店</button>
          </div>`,
      })}
      ${UI.win({
        title: `待開寶箱（${rewards.unopenedChests.length}）`,
        body: rewards.unopenedChests.length
          ? `<div class="chest-list">${rewards.unopenedChests.map((c) => `
              <button class="chest rarity-${c.rarity}" data-id="${c.id}">
                <span class="chest-rank">${c.rarity}</span><span class="chest-source">${h(CHEST_SOURCE[c.source] || c.source)}</span>
              </button>`).join('')}</div>`
          : '<p class="muted">目前沒有寶箱。升級、連續達標、完成目標都能獲得。</p>',
      })}
      ${UI.win({
        title: '成就',
        body: `<ul class="achievements">${rewards.achievements.map((a) => `
          <li class="${a.unlocked ? 'unlocked' : ''}">
            <div class="ach-name">${h(a.name)} <span class="chip">${a.slot === 'Prefix' ? '前綴' : '後綴'}「${h(a.titleText)}」</span></div>
            <div class="ach-cond">${h(a.condition)}</div>
            ${a.unlocked ? '' : `<div class="ach-bar"><i style="width:${Math.round((a.progress / a.target) * 100)}%"></i></div><div class="ach-progress">${a.progress} / ${a.target}</div>`}
          </li>`).join('')}</ul>`,
      })}
      ${UI.win({
        title: `圖鑑 ${cards.ownedKinds} / ${cards.total}`,
        body: `<div class="grid-cards">${sorted.map((c) => `
          <button class="card-cell" data-id="${h(c.id)}"${c.owned ? '' : ' disabled'}>
            ${cardFace(c, { owned: c.owned })}
            ${c.owned && c.count > 1 ? `<span class="card-count">×${c.count}</span>` : ''}
          </button>`).join('')}</div>`,
      })}`;

    const saveTitle = () => run(async () => {
      await ctx.api('PUT', '/me/title', {
        prefixKey: ctx.view.querySelector('#title-prefix').value || null,
        suffixKey: ctx.view.querySelector('#title-suffix').value || null,
      });
      await ctx.reload();
    });
    ctx.view.querySelector('#title-prefix').addEventListener('change', saveTitle);
    ctx.view.querySelector('#title-suffix').addEventListener('change', saveTitle);
    ctx.view.querySelector('#open-shop').addEventListener('click', () => openShop(ctx, rewards));
    ctx.view.querySelectorAll('.chest').forEach((b) => b.addEventListener('click', () => openChest(ctx, b.dataset.id)));
    ctx.view.querySelectorAll('.card-cell:not([disabled])').forEach((b) => b.addEventListener('click', () => {
      showCard(ctx, cards.cards.find((c) => c.id === b.dataset.id), rewards.pinnedCard?.id);
    }));
  }

  async function themeSection(ctx) {
    const rewards = await ctx.api('GET', '/rewards');
    const owned = new Set(rewards.ownedThemes);
    const html = UI.win({
      title: '主題',
      body: `<div class="theme-list">${THEMES.map((t) => `
        <div class="theme-card" data-theme-key="${t.key}">
          <span class="theme-swatch"></span><span class="theme-name">${t.name}</span>
          ${rewards.themeKey === t.key
            ? '<span class="chip">使用中</span>'
            : owned.has(t.key)
              ? `<button class="btn btn-ghost" data-use="${t.key}">套用</button>`
              : `<button class="btn btn-primary" data-buy="${t.key}">${t.price} 金幣</button>`}
        </div>`).join('')}</div>
        <p class="muted">金幣 ${rewards.coins}</p>`,
    });
    const bind = (root) => {
      root.querySelectorAll('[data-use]').forEach((b) => b.addEventListener('click', () => run(async () => {
        await ctx.api('PUT', '/me/theme', { themeKey: b.dataset.use });
        UI.setAccent(b.dataset.use);
        await ctx.reload();
      })));
      root.querySelectorAll('[data-buy]').forEach((b) => UI.confirmButton(b, b.textContent, () => run(async () => {
        const key = b.dataset.buy;
        await ctx.api('POST', '/shop/purchase', { item: 'Theme', themeKey: key });
        UI.sysMessage([`解鎖主題「${THEMES.find((t) => t.key === key).name}」。`, '按「套用」即可切換。']);
        await ctx.reload();
      })));
    };
    return { html, bind };
  }

  window.Hunter = { render, themeSection };
})();
```

- [ ] **Step 3: 載入順序**

用 Edit 修改 `src/SoloLeveling.Api/wwwroot/index.html`：在 `<script src="ui.js"></script>` 與 `<script src="app.js"></script>` 之間加入一行 `<script src="hunter.js"></script>`，結果為：

```html
  <script src="ui.js"></script>
  <script src="hunter.js"></script>
  <script src="app.js"></script>
```

- [ ] **Step 4: `app.js` 導覽、收集 rewards、系統訊息、路由、設定頁**

用 Edit 修改 `src/SoloLeveling.Api/wwwroot/app.js`：

1. `NAV_ITEMS` 在 `progress` 與 `settings` 之間插入檔案頁，結果為：

```js
  const NAV_ITEMS = [
    { route: 'today', zh: '今日', en: 'TODAY' },
    { route: 'progress', zh: '進度', en: 'STATS' },
    { route: 'hunter', zh: '檔案', en: 'HUNTER' },
    { route: 'settings', zh: '設定', en: 'SYS' },
  ];
```

2. `const state = { ... }` 物件加一個屬性 `pendingRewards: []`（例：`const state = { token: localStorage.getItem('token'), me: null, today: null, pendingRewards: [] };`）。

3. `api()` 內，在成功路徑最後的 `return data;` 之前加入：

```js
    // 每個會改狀態的回應都帶 rewards；集中收集，由 announceRewards 依序顯示一次
    if (data && data.rewards) {
      state.pendingRewards.push(data.rewards);
    }
```

4. 在 `function announce(` 定義之前加入：

```js
  const CHEST_SOURCE_ZH = {
    LevelUp: '升級',
    RankUp: '晉階',
    Streak7: '連續 7 天',
    Streak30: '連續 30 天',
    GoalCompleted: '目標完成',
    ProgramCompleted: '66 天完成',
    Purchase: '商店',
  };

  // 取出並清空 api() 收集到的 rewards；每筆只會顯示一次
  function drainRewards() {
    const list = state.pendingRewards;
    state.pendingRewards = [];
    return list;
  }

  // 依規格順序顯示：升級 → 晉階 → 寶箱 → 成就 → 保險卡生效
  function announceRewards(list, me) {
    if (!list || list.length === 0) {
      return;
    }
    const levels = list.reduce((sum, r) => sum + r.levelsGained, 0);
    const rankUps = list.flatMap((r) => r.rankUps);
    const shieldsGained = list.reduce((sum, r) => sum + r.shieldsGained, 0);
    const chests = list.flatMap((r) => r.newChests);
    const achievements = list.flatMap((r) => r.newAchievements);
    const shieldUses = list.filter((r) => r.shieldsUsed.length > 0);
    if (levels > 0) {
      UI.sysMessage([`等級提升。目前 Lv.${me?.player?.level ?? ''}。`]);
    }
    if (rankUps.length > 0) {
      UI.sysMessage(rankUps.map((rank) => `階級晉升：${rank} 級。`)
        .concat(shieldsGained > 0 ? [`獲得連勝保險卡 ${shieldsGained} 張。`] : []));
    }
    if (chests.length > 0) {
      UI.sysMessage(chests.map((c) => `獲得 ${c.rarity} 級寶箱（${CHEST_SOURCE_ZH[c.source] || c.source}）。`)
        .concat(['前往「檔案」開啟。']));
    }
    achievements.forEach((a) => UI.sysMessage([`成就解鎖「${a.name}」。`, `獲得稱號字塊「${a.titleText}」。`]));
    shieldUses.forEach((r) => UI.sysMessage([`連勝保險已生效，剩餘 ${r.shieldCount} 張。`]));
  }
```

5. `announce` 的簽章改成第 5 個參數預設取出待顯示的 rewards（既有呼叫端不用改）：

```js
  function announce(prevMe, prevToday, me, today, rewards = drainRewards()) {
```

在 `announce` 內刪除「比較 `prevMe.player.level` 與 `me.player.level` 後顯示升級訊息」的那個 `if` 區塊（用 `grep -n "player.level" src/SoloLeveling.Api/wwwroot/app.js` 找到；升級訊息改由 `rewards.levelsGained` 觸發，避免重複）。在 `announce` 函式本體的最後加入：

```js
    UI.setAccent(me?.player?.themeKey || 'azure');
    announceRewards(rewards, me);
```

若 `announce` 內有提早 `return` 的分支，把這兩行放在第一個 `return` 之前，確保每次呼叫都會執行。

6. `route()` 內，在 `await loadToday();` 之後加入：

```js
      UI.setAccent(state.me.player.themeKey || 'azure');
      // 切換畫面時 GET /me、/today 也可能帶回獎勵（例如排程結算消耗的保險卡）
      announceRewards(drainRewards(), state.me);
```

並在選擇畫面的分支（`if (hash === 'progress') …`）加入檔案頁：

```js
      else if (hash === 'hunter') {
        await Hunter.render({ api, view: $view, me: state.me, today: state.today, reload: route });
      }
```

（放在 `settings` 分支之前，維持既有的大括號風格；若既有分支沒有大括號，一併補上。）

7. `renderSettings()` 內：函式開頭（第一行 `renderHeader()` 或第一個 `await` 之前）加入

```js
    const theme = await Hunter.themeSection({ api, reload: route });
```

在設定頁指派給 `$view.innerHTML` 的樣板字串中，困難模式視窗（含 `id="hard"` 的那個視窗）結束之後插入 `${theme.html}`；在 `$view.innerHTML = …;` 這個敘述之後加入：

```js
    theme.bind($view);
```

- [ ] **Step 5: `ui.js` 狀態面板顯示釘選卡**

用 Edit 修改 `src/SoloLeveling.Api/wwwroot/ui.js`：

1. 在 `window.UI = {` 之前加入：

```js
  // 狀態面板名稱旁的釘選卡小圖；圖檔不存在時只剩稀有度色框
  function pinnedThumb(card) {
    if (!card) {
      return '';
    }
    return `<span class="pinned-thumb rarity-${card.rarity}" title="${window.UI.h(card.name)}"><img src="${window.UI.h(card.image)}" alt="" onerror="this.remove()"></span>`;
  }
```

2. 在 `statusPanel` 內輸出使用者名稱的地方（`grep -n "displayName" src/SoloLeveling.Api/wwwroot/ui.js`），緊接在名稱的 HTML 之後加上 `${pinnedThumb(me.player.pinnedCard)}`。

- [ ] **Step 6: 樣式**

用 Edit 在 `src/SoloLeveling.Api/wwwroot/app.css` 檔尾加入：

```css
/* ---------- 檔案 HUNTER（獎勵系統） ---------- */
:root {
  /* 主題卡色票，取自三個主題的 --accent */
  --swatch-azure: #5cb6ff;
  --swatch-violet: #a585ff;
  --swatch-jade: #4fdcaa;
}

.hcard {
  --card-rarity: var(--rarity-e);
  position: relative;
  aspect-ratio: 2 / 3;
  border-radius: 10px;
  overflow: hidden;
  background: linear-gradient(160deg, var(--card-rarity), var(--panel-solid) 70%);
  box-shadow: inset 0 0 0 1px var(--card-rarity), 0 0 16px -6px var(--card-rarity);
}
.hcard.rarity-C { --card-rarity: var(--rarity-c); }
.hcard.rarity-A { --card-rarity: var(--rarity-a); }
.hcard.rarity-S { --card-rarity: var(--rarity-s); }
.hcard img { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; }
.hcard-frame {
  position: absolute;
  left: 0;
  right: 0;
  bottom: 0;
  display: flex;
  gap: 6px;
  align-items: baseline;
  padding: 18px 8px 6px;
  background: linear-gradient(transparent, color-mix(in srgb, var(--bg) 85%, transparent));
}
.hcard-rarity { font-family: var(--latin); font-weight: 700; color: var(--card-rarity); }
.hcard-name { font-family: var(--serif); font-size: 12px; color: var(--ink); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.hcard-unknown { display: grid; place-items: center; background: var(--panel-solid); filter: grayscale(1); opacity: .55; }
.hcard-q { font-family: var(--serif); font-size: 28px; color: var(--muted); }
.hcard-lg { width: min(240px, 70vw); margin: 0 auto; }

.card-cell { position: relative; padding: 0; border: 0; background: none; min-height: 40px; cursor: pointer; touch-action: manipulation; }
.card-cell[disabled] { cursor: default; }
.card-count { position: absolute; top: 4px; right: 6px; font-family: var(--mono); font-size: 11px; color: var(--ink); }

.hunter-wallet { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; }
.title-now { font-family: var(--serif); font-size: 20px; text-align: center; margin-bottom: 8px; }
.title-picker { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
.title-picker select { width: 100%; min-height: 40px; }

.chest-list { display: flex; flex-wrap: wrap; gap: 8px; }
.chest {
  min-width: 72px;
  min-height: 72px;
  display: grid;
  place-items: center;
  gap: 2px;
  border-radius: 10px;
  border: 1px solid var(--line);
  background: var(--panel-solid);
  color: var(--ink);
  cursor: pointer;
  touch-action: manipulation;
}
.chest-rank { font-family: var(--latin); font-size: 22px; font-weight: 700; }
.chest-source { font-size: 11px; color: var(--muted); }

.achievements { list-style: none; margin: 0; padding: 0; display: grid; gap: 10px; }
.achievements li { opacity: .6; }
.achievements li.unlocked { opacity: 1; }
.ach-name { font-family: var(--serif); }
.ach-cond, .ach-progress { font-size: 12px; color: var(--muted); }
.ach-bar { height: 4px; border-radius: 2px; background: var(--line-soft); overflow: hidden; margin-top: 4px; }
.ach-bar i { display: block; height: 100%; background: var(--accent); }

.hunter-modal {
  position: fixed;
  inset: 0;
  z-index: 50;
  display: grid;
  place-items: center;
  padding: 16px 16px calc(16px + env(safe-area-inset-bottom));
  background: color-mix(in srgb, var(--bg) 80%, transparent);
}
.hunter-modal-inner { width: min(420px, 100%); max-height: 90vh; overflow-y: auto; display: grid; gap: 12px; text-align: center; }

.flip { perspective: 800px; width: min(240px, 70vw); margin: 0 auto; }
.flip-inner { position: relative; aspect-ratio: 2 / 3; transform-style: preserve-3d; transition: transform .7s ease; }
.flip.flipped .flip-inner { transform: rotateY(180deg); }
.flip-back, .flip-front { position: absolute; inset: 0; backface-visibility: hidden; }
.flip-front { transform: rotateY(180deg); }
.flip-front .hcard-lg { width: 100%; }
.flip-back {
  display: grid;
  place-items: center;
  border-radius: 10px;
  background: var(--panel-solid);
  box-shadow: inset 0 0 0 1px var(--line), 0 0 24px -8px var(--glow);
  font-family: var(--latin);
  font-size: 48px;
  color: var(--accent);
}

.shop-item, .theme-card { display: flex; justify-content: space-between; align-items: center; gap: 8px; padding: 8px 0; border-bottom: 1px solid var(--line-soft); }
.theme-swatch { width: 24px; height: 24px; border-radius: 50%; flex: none; }
.theme-card[data-theme-key="azure"] .theme-swatch { background: var(--swatch-azure); }
.theme-card[data-theme-key="violet"] .theme-swatch { background: var(--swatch-violet); }
.theme-card[data-theme-key="jade"] .theme-swatch { background: var(--swatch-jade); }
.theme-name { flex: 1; text-align: left; }

.pinned-thumb {
  display: inline-block;
  width: 24px;
  aspect-ratio: 2 / 3;
  border-radius: 3px;
  overflow: hidden;
  vertical-align: middle;
  margin-left: 6px;
  background: var(--panel-solid);
  box-shadow: inset 0 0 0 1px var(--line);
}
.pinned-thumb img { width: 100%; height: 100%; object-fit: cover; }

@media (prefers-reduced-motion: reduce) {
  .flip-inner { transition: none; }
}
```

- [ ] **Step 7: 語法檢查與後端測試**

Run: `node --check src/SoloLeveling.Api/wwwroot/hunter.js && node --check src/SoloLeveling.Api/wwwroot/app.js && node --check src/SoloLeveling.Api/wwwroot/ui.js`
Expected: 無輸出（語法正確）。沒有安裝 Node 時略過此步，改在 Step 8 的瀏覽器主控台確認沒有 `SyntaxError`。

Run: `dotnet test`
Expected: 全部 PASS（前端改動不影響後端）。

- [ ] **Step 8: 手動驗收（瀏覽器）**

Run: `docker compose up -d --build`，開 `http://localhost:8080/`（375px 寬的手機模擬也看一次）。
Expected，逐項確認：
1. 新帳號完成引導後，勾完 3 個困難任務（約 105 EXP）時依序跳出「等級提升」與「獲得 E 級寶箱」兩則系統訊息。
2. 導覽出現第三格「檔案 HUNTER」；檔案頁有待開寶箱，點開有翻牌動效與「獲得 E 級卡片」訊息；圖鑑該卡亮起（沒有插畫時顯示稀有度色塊與名稱），未擁有的卡是「？」剪影。
3. 點已擁有的卡可看大圖、風味文字、首次取得日，「釘選展示」後檔案頁頂端與狀態面板名稱旁出現小圖；再按可取消。
4. 商店小視窗有三種商品；金幣不足時購買出現紅色錯誤 toast。
5. 設定頁「主題」：藍光顯示使用中；以 SQL 給自己 1000 金幣（`docker compose exec postgres psql -U postgres sololeveling -c "UPDATE \"Players\" SET \"Coins\" = 1000;"`）後買紫影（二段式確認），按「套用」後介面整體換成紫色，重新整理仍保持紫影。
6. 以 SQL 解鎖兩個字塊（`INSERT INTO "Achievements" ("UserId","Key","UnlockedAt") SELECT "UserId", k, 0 FROM "Players", unnest(ARRAY['bed-30','quests-100']) k;`）後，檔案頁選前綴「靜夜的」、後綴「百戰獵人」，狀態面板顯示「靜夜的・百戰獵人」。
7. 主控台沒有錯誤；`prefers-reduced-motion` 開啟時翻牌無動畫。

- [ ] **Step 9: Commit**

```bash
git add src/SoloLeveling.Api/wwwroot
git commit -F - <<'EOF'
feat: 前端新增檔案頁、商店、主題設定與獎勵系統訊息

1. 檔案頁集中稱號組合、寶箱、成就與圖鑑，插畫缺檔時顯示佔位卡
2. api() 統一收集 rewards，依升級、晉階、寶箱、成就、保險卡的順序顯示

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NxkXCvcUbg2y9cvwZjMor4
EOF
```

---

### Task 9: 文件：SPEC、ARCHITECTURE、README、CLAUDE.md

**Files:**
- Modify: `docs/SPEC.md`（新增 4.12、第 5／6／7／11 節）
- Modify: `docs/ARCHITECTURE.md`（第 2／3／4／5／9 節）
- Modify: `README.md`（API 範例、實作上的決定、後續）
- Modify: `CLAUDE.md`（關鍵不變式、已知陷阱、目前狀態）

**Interfaces:**
- Consumes: Task 1–8 的實際名稱與行為。
- Produces: 無程式介面。

- [ ] **Step 1: SPEC.md**

用 Edit 在 `docs/SPEC.md` 的 `## 5. 資料模型（EF Core 實體）` 之前插入：

```markdown
### 4.12 獎勵系統（寶箱、卡片、金幣、連勝保險、稱號、主題）

獎勵只跟努力綁定，不能用金幣換 EXP、等級或連勝天數。

**寶箱**（建立時未開啟，使用者手動開）

| 觸發 | 寶箱 | 判定 |
| --- | --- | --- |
| 每升 1 級 | E | 只對超過 `Player.PeakLevel` 的等級發，撤銷降級後再升回來不重發 |
| 階級晉升 | C，另送 1 張保險卡（上限 3） | 新等級與前一級的 `RankOf` 不同 |
| 最佳連續達 7 天 | C | 成就「不屈」首次解鎖時 |
| 最佳連續達 30 天 | A | 成就「恆心」首次解鎖時 |
| 目標完成 | A | 目標每個未封存任務的達標天數（含今天）≥ `(StageCount − 1) × DaysPerStep + 1`；寫入 `Goal.CompletedAt` 後不再判定 |
| 66 天週期完成 | S | `POST /program/restart` 時 `today − StartDate ≥ LengthDays`，寫入 `Program.CompletedAt` |
| 金幣購買 | E | 200 金幣 |

**開箱**：從該等級全部卡片均勻抽一張（含已擁有）；未擁有加入收藏，已擁有張數 +1 並轉成重複金幣；另附開箱金幣。E 20／30、C 50／80、A 100／200、S 300／500（開箱／重複）。亂數來源注入。

**卡片**：目錄在 `Cards.All`（E 10、C 8、A 5、S 2），`Image` 為 `cards/{id}.webp`；插畫規格 2:3、768×1152、WebP 或 PNG、不含邊框與文字，檔案不存在時前端顯示佔位卡。

**金幣**（每次變動一筆 `CoinEvent`，經 `Wallet`）：今日首次達標 +10（`DailyLog.ClearCoinsGranted` 跟著 `BonusGranted`；退回達標時收回，最多扣到 0）；開箱與重複卡見上；解鎖成就 +50。商店：保險卡 100（最多持有 3）、E 級寶箱 200、主題紫影／翡翠各 500（藍光免費預設）。錯誤碼 `NotEnoughCoins`、`ShieldLimitReached`、`ThemeOwned`、`UnknownTheme`（皆 400）。

**連勝保險卡**：結算時某天未達標、`Streak > 0` 且 `ShieldCount > 0` → 消耗 1 張、Streak +1、記一筆 `RewardEvent(ShieldUsed, date)`；困難模式懲罰照扣；補多天時逐日判定。事件在下一個套用獎勵的請求回報一次（`AnnouncedAt`）。

**成就與稱號**：目錄在 `Achievements.All`（12 個），條件成立且未解鎖即解鎖（唯一主鍵擋重複），+50 金幣，解鎖一個字塊。任務角色由漸進任務推導：作息＋`TimeOfDay` → 就寢、作息＋`TimeOfDayEvening` → 起床、運動／閱讀／螢幕時間同名。分類連續天數＝未封存角色任務「到昨天為止連續完成天數，今天完成再 +1」；分類累計分鐘＝運動、閱讀任務（含已封存）所有進度值加總。稱號：`Player.TitlePrefixKey`／`TitleSuffixKey` 只能選已解鎖且槽位正確的字塊；有選 → 「前綴・後綴」（只選一邊顯示該邊），都沒選 → 階級稱號。

**主題**：`Player.ThemeKey`（`azure`／`violet`／`jade`，預設 `azure`），已購買記在 `OwnedThemes`。

**判定時機**：所有會改玩家狀態的端點（與 `GET /today`、`GET /me`）在第一次 `SaveChanges` 之後呼叫一次 `RewardApplier`（`Rewards.Evaluate`），結果寫入 DB 並放進回應的 `rewards` 欄位；排程結算不發寶箱，等下一次請求補判定。

```

把第 5 節表格中的以下列整列換掉（用 Edit 逐列）：

- `| Player | … |` 那一列結尾的 `LastSettledDate DateOnly nullable |` 換成 `LastSettledDate DateOnly nullable；PeakLevel int（曾到達的最高等級）；Coins int ≥ 0；ShieldCount int 0–3；ThemeKey（≤ 16，預設 azure）；TitlePrefixKey／TitleSuffixKey（≤ 32）nullable；PinnedCardId（≤ 64）nullable |`
- `| Program | … |` 那一列結尾的 `（partial unique index） |` 換成 `（partial unique index）；CompletedAt bigint nullable |`
- `| Goal | … |` 那一列結尾的 `ArchivedAt bigint nullable；CreatedAt bigint |` 換成 `ArchivedAt bigint nullable；CompletedAt bigint nullable；CreatedAt bigint |`
- `| DailyLog | … |` 那一列的 `BonusGranted bool；` 換成 `BonusGranted bool；ClearCoinsGranted bool；`

在 `| XpEvent | … |` 那一列之後加入：

```markdown
| RewardChest | Id；UserId（FK）；Rarity enum（E／C／A／S）；Source enum {LevelUp, RankUp, Streak7, Streak30, GoalCompleted, ProgramCompleted, Purchase}；CreatedAt bigint；OpenedAt bigint nullable；DroppedCardId（≤ 64）nullable；Coins int |
| OwnedCard | PK (UserId, CardId)；Count int ≥ 1；FirstAcquiredAt bigint |
| Achievement | PK (UserId, Key)；UnlockedAt bigint |
| OwnedTheme | PK (UserId, ThemeKey) |
| CoinEvent | Id；Seq bigint identity；UserId（FK）；Amount int（可負）；Source enum {DailyClear, DailyClearUndo, ChestOpen, DuplicateCard, Achievement, ShopShield, ShopChest, ShopTheme}；RefId Guid nullable；OccurredAt bigint |
| RewardEvent | Id；UserId（FK）；Kind enum {ShieldUsed}；Date DateOnly；OccurredAt bigint；AnnouncedAt bigint nullable |
```

把「索引：」那一行換成：

```markdown
索引：`XpEvent(UserId, OccurredAt)`、`Quest(UserId, IsArchived)`、`DailyLog(UserId, Date)`、`Goal(UserId, IsArchived)`、`Goal` 部分唯一索引 `(UserId, Category) WHERE IsArchived = false`、`RewardChest(UserId, OpenedAt)`、`CoinEvent(UserId, OccurredAt)`、`RewardEvent(UserId, AnnouncedAt)`。
```

第 6 節表格（各列以開頭的「方法與路徑」欄定位）：

- GET /me 那一列：回應欄的 `hardMode, displayStreak, bestStreak, totalCompleted}` 換成 `hardMode, displayStreak, bestStreak, totalCompleted, rankTitle, coins, shieldCount, themeKey, pinnedCard}`，該欄最後補「；title 為組合後稱號，rewards 見表後說明」。
- DELETE /goals/{id} 那一列整列換成：

```markdown
| DELETE /goals/{id} | 封存目標與其任務 | — | 200 `{rewards}`；重算今日達標率 |
```

- DELETE /quests/{id} 那一列整列換成：

```markdown
| DELETE /quests/{id} | 封存 | — | 200 `{rewards}`；漸進任務可單獨封存 |
```

- 在 POST /program/restart 那一列之後加入：

```markdown
| GET /rewards | 獎勵總覽 | — | `{coins, shieldCount, unopenedChests, achievements:[{key, name, condition, titleText, slot, unlocked, progress, target, unlockedAt}], titleFragments, titlePrefixKey, titleSuffixKey, title, pinnedCard, themeKey, ownedThemes}` |
| POST /rewards/chests/{id}/open | 開箱 | — | `{card, isDuplicate, coins, coinBalance, rewards}`；已開或別人的寶箱 404 `ChestNotFound` |
| GET /cards | 圖鑑 | — | `{cards:[{id, name, rarity, flavor, image, owned, count, firstAcquiredAt}], ownedKinds, total}` |
| POST /shop/purchase | 商店 | `{item: "Shield"｜"EChest"｜"Theme", themeKey?}` | `{coins, shieldCount, ownedThemes, chest, rewards}`；400 `NotEnoughCoins`／`ShieldLimitReached`／`ThemeOwned`／`UnknownTheme` |
| PUT /me/title | 稱號組合 | `{prefixKey, suffixKey}` | 200 同 GET /me；400 `TitleNotUnlocked` |
| PUT /me/pinned-card | 釘選卡 | `{cardId｜null}` | 200 同 GET /me；400 `CardNotOwned` |
| PUT /me/theme | 切換主題 | `{themeKey}` | 200 同 GET /me；400 `ThemeNotOwned` |
```

並在第 6 節表格之後加一段：

```markdown
`GET /today`、`PUT /today/quests/{id}/progress`、`POST/PUT /quests`、`DELETE /quests/{id}`、`POST /goals`、`DELETE /goals/{id}`、`GET/PATCH /me`、`PUT /me/*`、`POST /program/restart` 的回應帶 `rewards`：`{levelsGained, rankUps, newChests, newAchievements, coinDelta, shieldsGained, shieldsUsed, shieldCount}`；清單與 `GET /goals` 不帶此欄位。
```

第 7 節虛擬碼：把

```
          else:
              player.Streak = 0
              if player.HardMode && penaltiesApplied < 3:
```

換成：

```
          else:
              if player.ShieldCount > 0 && player.Streak > 0:
                  player.ShieldCount -= 1; player.Streak += 1
                  加入 RewardEvent(ShieldUsed, date)        // 下一個套用獎勵的請求回報
              else:
                  player.Streak = 0
              if player.HardMode && penaltiesApplied < 3:   // 用了保險卡也照扣
```

第 11 節在 `- [ ] 所有測試綠燈；` 那一行之前加入：

```markdown
- [ ] 新帳號從 Lv.1 升到 Lv.2 時看到「等級提升」與「獲得 E 級寶箱」兩則系統訊息，檔案頁可開箱並在圖鑑看到卡片。
- [ ] 漏一天且持有保險卡時，隔天連勝不中斷並看到保險生效訊息。
- [ ] 用金幣買到紫影後可切換，介面整體換色。
- [ ] 稱號可組合成「靜夜的・百戰獵人」並顯示在狀態面板。
```

- [ ] **Step 2: ARCHITECTURE.md**

用 Edit 修改 `docs/ARCHITECTURE.md`：

（以下各表格列以第一欄的路徑定位。）

1. Domain 表格中第一欄為 ``Enums.cs`` 的那一列整列換成：

```markdown
| `Enums.cs` | `StatType`、`Difficulty`、`QuestType`、`XpSource`、`GoalCategory`、`ProgressionValueKind`；獎勵：`Rarity`、`ChestSource`、`CoinSource`、`RewardEventKind`、`TitleSlot`、`QuestRole`、`ShopItem` |
```

並在第一欄為 ``DefaultQuests.cs`` 的那一列之後加入：

```markdown
| `Cards.cs`、`Achievements.cs` | 卡片目錄 `Cards.All`（25 張）；成就目錄 `Achievements.All`（12 個）與 `AchievementStats`（判定用統計快照） |
| `Themes.cs`、`Shop.cs` | 主題鍵（預設 azure）；商店價格與保險卡上限 3 |
| `Entities/RewardChest.cs` 等 | `RewardChest`、`OwnedCard`、`Achievement`、`OwnedTheme`、`CoinEvent`、`RewardEvent`；`Player` 加 PeakLevel、Coins、ShieldCount、ThemeKey、稱號字塊、PinnedCardId |
```

在第一欄為 ``Rules/UserClock.cs`` 的那一列之後加入：

```markdown
| `Rules/Rewards.cs` | `Evaluate(RewardInput) → RewardOutcome`：純函式判定寶箱、晉階、成就、達標金幣、保險卡；升級以 `PeakLevel` 判定 |
| `Rules/Loot.cs`、`Rules/Wallet.cs` | 開箱抽卡（亂數注入）；金幣異動的唯一入口，每次回傳 `CoinEvent` |
| `Rules/QuestRoles.cs`、`Rules/GoalCompletion.cs`、`Rules/Titles.cs` | 任務角色與分類連續天數；目標完成判定；稱號組合 |
```

並把第一欄為 ``Rules/Settlement.cs`` 的那一列整列換成：

```markdown
| `Rules/Settlement.cs` | `Settle`：結算演算法的純規則部分，回傳 `SettlementResult(Today, TodayLog, NewLogs, Events, ShieldsUsed)`；未達標日在連勝進行中且有保險卡時自動消耗；常數 `MaxCatchUpDays = 400`、`MaxPenaltiesPerSettlement = 3` |
```

2. Infrastructure 表格中第一欄為 ``SettlementService.cs`` 的那一列，第二欄最後補「；把 `ShieldsUsed` 寫成未公告的 `RewardEvent`」；第一欄為 ``Migrations/`` 的那一列整列換成：

```markdown
| `Migrations/` | EF Core migration（`InitialCreate`、`AddGoalsAndProgression`、`AddRewards`） |
```

3. Api 表格中第一欄為 ``Controllers/ProgramController.cs`` 的那一列之後加入：

```markdown
| `Controllers/RewardsController.cs` | `GET /rewards`、`POST /rewards/chests/{id}/open`、`GET /cards`、`POST /shop/purchase`；`MeController` 另有 `PUT /me/title`、`/me/pinned-card`、`/me/theme` |
```

在第一欄為 ``Services/ProgramService.cs`` 的那一列之後加入：

```markdown
| `Services/RewardApplier.cs` | `ApplyAsync(context, completedPrograms, ct)`：在第一次 SaveChanges 之後判定目標完成、查統計、`Rewards.Evaluate`、寫寶箱／成就／CoinEvent、回報保險卡事件，回傳 `RewardsDto` |
| `Services/RewardStatsLoader.cs` | 成就統計的批次查詢（固定 7 次，不隨任務數增加） |
| `Services/RewardService.cs` | 獎勵總覽、開箱、圖鑑、商店 |
| `Contracts/RewardDtos.cs` | 獎勵相關 DTO；`RewardsDto` 以選填 `Rewards` 參數掛在既有回應上（null 時 JSON 省略） |
```

並把第一欄為 ``wwwroot/`` 的那一列整列換成：

```markdown
| `wwwroot/` | `index.html`、`ui.js`、`hunter.js`（檔案頁、商店、主題）、`app.js`、`app.css`、`cards/`（卡片插畫，可缺） |
```

4. 第 3 節第 7 點 `7. **持久化**：…` 之後插入新的一點，原 8、9 點順延為 9、10：

```markdown
8. **套用獎勵**：`RewardApplier.ApplyAsync`（仍在交易內）：判定目標完成並寫 `CompletedAt` → `RewardStatsLoader` 批次查統計（此時看得到剛存的進度）→ `Rewards.Evaluate`（請求前快照 `TodayContext.Before` 只用來算 `levelsGained`）→ 寫寶箱、成就、`CoinEvent`（經 `Wallet`）、更新 `PeakLevel`／`ShieldCount`／`ClearCoinsGranted` → 未公告的保險卡事件標成已公告 → `SaveChangesAsync`。回傳的 `RewardsDto` 放進回應的 `rewards`。
```

5. 第 4 節「演算法重點」清單中 `- 達標 Streak + 1，未達標歸零；BestStreak 取最大值。` 換成：

```markdown
- 達標 Streak + 1；未達標時若連勝進行中（Streak > 0）且有保險卡，消耗 1 張、Streak + 1 並回傳在 `ShieldsUsed`（`SettlementService` 寫成 `RewardEvent`），否則歸零；BestStreak 取最大值。保險卡由排程消耗時，下一次套用獎勵的請求回報一次。
```

6. 第 5 節表格在第一欄為 ``XpEvents`` 的那一列之後加入：

```markdown
| `RewardChests` | 寶箱 | index `(UserId, OpenedAt)`；`Rarity`、`Source` 存字串 |
| `OwnedCards` | 擁有的卡片 | PK `(UserId, CardId)` |
| `Achievements` | 已解鎖成就 | PK `(UserId, Key)`，擋重複解鎖 |
| `OwnedThemes` | 已購買主題 | PK `(UserId, ThemeKey)` |
| `CoinEvents` | 金幣流水 | index `(UserId, OccurredAt)`；`Seq` identity |
| `RewardEvents` | 保險卡生效等需通知的事件 | index `(UserId, AnnouncedAt)` |
```

7. 第 9 節樣板程式碼

```csharp
   db.XpEvents.AddRange(events);
   // 透過導覽集合新增的實體要明確 AddRange
   await db.SaveChangesAsync(ct);
   await tx.CommitAsync(ct);
```

換成：

```csharp
   db.XpEvents.AddRange(events);
   // 透過導覽集合新增的實體要明確 AddRange
   await db.SaveChangesAsync(ct);
   // 會改玩家狀態的端點一律套用獎勵（存檔之後，統計才看得到本次修改），結果放進回應的 Rewards
   var rewards = await rewardApplier.ApplyAsync(context, 0, ct);
   await tx.CommitAsync(ct);
```

- [ ] **Step 3: README.md**

用 Edit 在 `README.md` API 範例的 `# 開新 66 天` 那兩行之後、結尾 ` ``` ` 之前加入：

```bash

# 獎勵總覽、圖鑑、開箱
curl -s localhost:8080/api/v1/rewards -H "Authorization: Bearer $TOKEN"
curl -s localhost:8080/api/v1/cards -H "Authorization: Bearer $TOKEN"
curl -s -X POST localhost:8080/api/v1/rewards/chests/<chestId>/open -H "Authorization: Bearer $TOKEN"

# 商店（item：Shield／EChest／Theme）
curl -s -X POST localhost:8080/api/v1/shop/purchase -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"item":"Theme","themeKey":"violet"}'

# 稱號組合、主題
curl -s -X PUT localhost:8080/api/v1/me/title -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"prefixKey":"bed-30","suffixKey":"quests-100"}'
curl -s -X PUT localhost:8080/api/v1/me/theme -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"themeKey":"violet"}'
```

在「實作上的決定」清單最後（`- **Email 重複**：…` 之後）加入：

```markdown
- **升級寶箱以 PeakLevel 判定**：只對超過歷史最高等級的等級發 E 箱與晉階獎勵，撤銷降級後再升回來不重發；migration 把既有玩家的 PeakLevel 設為當時等級。
- **成就以狀態判定**：條件成立且未解鎖就解鎖，因此上線前已達成的條件會在下一次請求補解鎖一次（含連續 7／30 天的寶箱）。
- **達標金幣收回最多扣到 0**：金幣可能已花掉，收回時不讓餘額變負，事件金額等於實際扣除量。
- **保險卡只在連勝進行中消耗**：Streak 為 0 時沒有東西可保護，不消耗；晉階送的保險卡受上限 3 截斷。
- **封存回 200**：`DELETE /quests/{id}`、`DELETE /goals/{id}` 回 `{ rewards }`，封存造成的達標金幣與升級訊息才不會遺失。
- **卡片插畫**：放 `wwwroot/cards/{id}.webp`（2:3、768×1152，不含邊框與文字）；缺檔時前端顯示稀有度色塊與名稱。
```

把「後續」第一行的 `推播、好友、排名、卡片、專注計時器` 換成 `推播、好友、排名、專注計時器`，並在「後續」清單最後加入：

```markdown
- 獎勵系統之後可能加：地下城、66 天 Boss、更多主題、稱號特效、以金幣兌換指定卡片。
```

- [ ] **Step 4: CLAUDE.md**

用 Edit 修改 `CLAUDE.md`：

1. 「關鍵不變式」中 `- **Player.Xp 的任何變動都必須對應一筆 XpEvent。**` 那一條之後加入：

```markdown
- **Player.Coins 的任何變動都必須對應一筆 CoinEvent，一律經 `Wallet.Change`／`Wallet.Spend`。** 不要直接改 `Player.Coins`（測試種資料除外）。
- **會改玩家狀態的端點在第一次 `SaveChangesAsync` 之後呼叫一次 `RewardApplier.ApplyAsync`，再 commit。** 它會查統計（要看得到本次修改）、寫寶箱／成就／金幣事件、回報保險卡事件並再存一次；回傳的 `RewardsDto` 放進回應的 `Rewards`。只讀的 `GET /rewards`、`/goals`、`/history` 不呼叫（會吃掉待公告的保險卡事件）。
- **升級寶箱與晉階獎勵以 `Player.PeakLevel` 判定**，成就以「條件成立且未解鎖」判定；不要改成請求前後差，否則撤銷再完成可以刷寶箱。
```

2. 「已知陷阱」最後加入：

```markdown
- **`Settlement.Settle` 會消耗保險卡**（未達標、Streak > 0、ShieldCount > 0），`SettlementService` 把 `ShieldsUsed` 寫成未公告的 `RewardEvent`；排程結算也會走到這裡。
- **`Achievements` 同名**：`SoloLeveling.Domain.Achievements` 是成就目錄（static），`AppDbContext.Achievements` 是已解鎖成就的 DbSet（實體 `Achievement`）。
```

3. 「目前狀態」第一行 `- MVP 規格全部完成，測試全綠（109 個）。` 換成 `- MVP、引導式目標、系統介面改版、獎勵系統完成，測試全綠（<N> 個）。`，其中 `<N>` 填入本步驟執行 `dotnet test` 時輸出的 Domain 與 Api 測試總數（兩個專案 `Passed:` 之和）。

- [ ] **Step 5: 檢查並 Commit**

Run: `dotnet test`
Expected: 全部 PASS；記下兩個專案的 `Passed:` 數字填入 CLAUDE.md。

Run: `grep -n "204；重算\|204；漸進" docs/SPEC.md`
Expected: 無輸出（封存已改為 200）。

```bash
git add docs/SPEC.md docs/ARCHITECTURE.md README.md CLAUDE.md
git commit -F - <<'EOF'
docs: 補上獎勵系統的規格、架構與開發不變式

1. SPEC 新增 4.12 與資料表、端點、結算保險卡步驟、驗收項目
2. CLAUDE.md 加入金幣事件與 RewardApplier 呼叫時機的不變式

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NxkXCvcUbg2y9cvwZjMor4
EOF
```

