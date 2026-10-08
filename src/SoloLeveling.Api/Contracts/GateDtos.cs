using System.Text.Json.Serialization;
using SoloLeveling.Domain;

namespace SoloLeveling.Api.Contracts;

/// <summary>進行中的傳送門。</summary>
/// <param name="Rank">等級。</param>
/// <param name="StartDate">開門日（第 1 天）。</param>
/// <param name="DayNumber">今日是第幾天，夾在 1 到 <paramref name="LengthDays"/>。</param>
/// <param name="LengthDays">總天數。</param>
/// <param name="Misses">已漏天數。</param>
/// <param name="AllowedMisses">最多可漏天數。</param>
/// <param name="Status">狀態（進行中的門為 Active）。</param>
/// <param name="Rewards">本次請求的獎勵；只有 POST /gates 帶，其他地方為 null 並從 JSON 省略。</param>
public record ActiveGateDto(
    GateRank Rank,
    DateOnly StartDate,
    int DayNumber,
    int LengthDays,
    int Misses,
    int AllowedMisses,
    GateStatus Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RewardsDto? Rewards = null);

/// <summary>等級表的一列，含玩家的解鎖狀態與通關次數。</summary>
/// <param name="Rank">等級。</param>
/// <param name="LengthDays">天數。</param>
/// <param name="AllowedMisses">最多可漏天數。</param>
/// <param name="Chests">每次通關發的寶箱等級。</param>
/// <param name="AchievementName">首次通關解鎖的成就名稱。</param>
/// <param name="TitleText">首次通關解鎖的稱號字塊。</param>
/// <param name="RequiredRank">需先通關的等級；E 級為 null。</param>
/// <param name="Unlocked">是否已解鎖。</param>
/// <param name="ClearCount">通關次數。</param>
public record GateTierDto(
    GateRank Rank,
    int LengthDays,
    int AllowedMisses,
    List<Rarity> Chests,
    string AchievementName,
    string TitleText,
    GateRank? RequiredRank,
    bool Unlocked,
    int ClearCount);

/// <summary>GET /gates。</summary>
/// <param name="Tiers">六級門，依等級表順序。</param>
/// <param name="ActiveGate">進行中的門；沒有為 null。</param>
/// <param name="Rewards">本次讀取補判定的獎勵（可能含傳送門通關或失敗）。</param>
public record GatesResponse(List<GateTierDto> Tiers, ActiveGateDto? ActiveGate, RewardsDto Rewards);

/// <summary>POST /gates。</summary>
/// <param name="Rank">要開的等級；缺漏或未定義的值回 400 <c>InvalidGateRank</c>。</param>
public record OpenGateRequest(GateRank? Rank);
