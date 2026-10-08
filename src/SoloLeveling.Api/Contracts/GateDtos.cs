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
