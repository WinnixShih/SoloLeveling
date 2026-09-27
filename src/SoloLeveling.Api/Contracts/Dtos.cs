using SoloLeveling.Domain;

namespace SoloLeveling.Api.Contracts;

/// <summary>統一錯誤回應。</summary>
/// <param name="Error">錯誤內容。</param>
public record ErrorResponse(ErrorBody Error);

/// <summary>錯誤內容。</summary>
/// <param name="Code">機器可讀的錯誤代碼。</param>
/// <param name="Message">給人看的訊息。</param>
public record ErrorBody(string Code, string Message);

/// <summary>註冊請求。</summary>
/// <param name="Email">Email。</param>
/// <param name="Password">密碼，至少 8 碼。</param>
/// <param name="DisplayName">顯示名稱，1–40 字。</param>
/// <param name="TimeZoneId">IANA 時區；省略為 <c>Asia/Taipei</c>。</param>
public record RegisterRequest(string Email, string Password, string DisplayName, string? TimeZoneId);

/// <summary>登入請求。</summary>
/// <param name="Email">Email。</param>
/// <param name="Password">密碼。</param>
public record LoginRequest(string Email, string Password);

/// <summary>註冊／登入回應。</summary>
/// <param name="Token">JWT 存取權杖。</param>
/// <param name="User">使用者資料。</param>
public record AuthResponse(string Token, UserDto User);

/// <summary>使用者資料。</summary>
/// <param name="Id">使用者 ID。</param>
/// <param name="Email">Email。</param>
/// <param name="DisplayName">顯示名稱。</param>
/// <param name="TimeZoneId">IANA 時區。</param>
public record UserDto(Guid Id, string Email, string DisplayName, string TimeZoneId);

/// <summary>五維屬性。</summary>
/// <param name="Str">力量。</param>
/// <param name="Vit">體力。</param>
/// <param name="Int">智力。</param>
/// <param name="Wil">意志。</param>
/// <param name="Spi">精神。</param>
public record StatsDto(int Str, int Vit, int Int, int Wil, int Spi);

/// <summary>玩家狀態。</summary>
/// <param name="Level">等級。</param>
/// <param name="Xp">目前等級內的 EXP。</param>
/// <param name="XpNeeded">升到下一級所需 EXP。</param>
/// <param name="Rank">階級（E–S）。</param>
/// <param name="Title">稱號。</param>
/// <param name="Stats">五維屬性。</param>
/// <param name="HardMode">是否困難模式。</param>
/// <param name="DisplayStreak">顯示用連續天數（含今日即時值）。</param>
/// <param name="BestStreak">最高連續天數。</param>
/// <param name="TotalCompleted">累計完成任務次數。</param>
public record PlayerDto(int Level, int Xp, int XpNeeded, string Rank, string Title, StatsDto Stats, bool HardMode, int DisplayStreak, int BestStreak, int TotalCompleted);

/// <summary>66 天計畫。</summary>
/// <param name="StartDate">起始日。</param>
/// <param name="Cycle">第幾週期。</param>
/// <param name="DayNumber">今日是第幾天（從 1 起）。</param>
/// <param name="LengthDays">週期長度。</param>
/// <param name="IsCompleted">是否已超過週期長度。</param>
public record ProgramDto(DateOnly StartDate, int Cycle, int DayNumber, int LengthDays, bool IsCompleted);

/// <summary>玩家總覽。</summary>
/// <param name="User">使用者。</param>
/// <param name="Player">玩家狀態。</param>
/// <param name="Program">66 天計畫。</param>
public record MeResponse(UserDto User, PlayerDto Player, ProgramDto Program);

/// <summary>更新設定請求；省略的欄位不動。</summary>
/// <param name="DisplayName">顯示名稱。</param>
/// <param name="TimeZoneId">IANA 時區。</param>
/// <param name="HardMode">困難模式。</param>
public record PatchMeRequest(string? DisplayName, string? TimeZoneId, bool? HardMode);

/// <summary>新增／修改任務請求。</summary>
/// <param name="Name">名稱，1–60 字。</param>
/// <param name="StatType">屬性代碼（STR／VIT／INT／WIL／SPI）。</param>
/// <param name="Difficulty">難度。</param>
/// <param name="QuestType">任務類型。</param>
/// <param name="TargetValue">目標值；Count／Limit 必填。</param>
/// <param name="Step">增減量。</param>
/// <param name="Unit">單位，最長 10 字。</param>
public record QuestRequest(string Name, StatType StatType, Difficulty Difficulty, QuestType QuestType, decimal? TargetValue, decimal? Step, string? Unit);

/// <summary>任務。</summary>
/// <param name="Id">任務 ID。</param>
/// <param name="Name">名稱。</param>
/// <param name="StatType">屬性代碼。</param>
/// <param name="Difficulty">難度。</param>
/// <param name="QuestType">任務類型。</param>
/// <param name="TargetValue">目標值。</param>
/// <param name="Step">增減量。</param>
/// <param name="Unit">單位。</param>
/// <param name="SortOrder">顯示順序。</param>
public record QuestDto(Guid Id, string Name, StatType StatType, Difficulty Difficulty, QuestType QuestType, decimal? TargetValue, decimal? Step, string? Unit, int SortOrder);

/// <summary>任務排序請求。</summary>
/// <param name="QuestIds">依新順序排列的全部未封存任務 ID。</param>
public record ReorderRequest(List<Guid> QuestIds);
