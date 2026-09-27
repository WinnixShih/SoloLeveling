using Microsoft.EntityFrameworkCore;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Api.Errors;
using SoloLeveling.Infrastructure;

namespace SoloLeveling.Api.Services;

/// <summary>
/// 每日紀錄與 EXP 流水查詢。
/// </summary>
/// <param name="db">DbContext。</param>
/// <param name="loader">今日內容載入（含結算）。</param>
public class HistoryService(AppDbContext db, TodayContextLoader loader)
{
    /// <summary>區間最多天數。</summary>
    public const int MaxRangeDays = 100;

    /// <summary>EXP 流水預設筆數。</summary>
    public const int DefaultEventLimit = 50;

    /// <summary>EXP 流水最多筆數。</summary>
    public const int MaxEventLimit = 200;

    /// <summary>
    /// GET /history：先結算，再回傳區間內每一天的紀錄（含今日即時值），日期由舊到新。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="from">起日（含）。</param>
    /// <param name="to">迄日（含）。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>紀錄清單。</returns>
    /// <exception cref="ApiErrorException">from 晚於 to，或區間超過 100 天（400）。</exception>
    public async Task<List<HistoryDayDto>> GetHistoryAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (from > to)
        {
            throw ApiErrorException.BadRequest("InvalidRange", "from 不可晚於 to");
        }

        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
        {
            throw ApiErrorException.BadRequest("RangeTooLarge", $"區間最多 {MaxRangeDays} 天");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await loader.LoadAsync(userId, ct);
        await tx.CommitAsync(ct);

        var logs = await db.DailyLogs.AsNoTracking()
            .Include(l => l.Progresses)
            .Where(l => l.UserId == userId && l.Date >= from && l.Date <= to)
            .OrderBy(l => l.Date)
            .ToListAsync(ct);

        return logs
            .Select(l => new HistoryDayDto(
                l.Date,
                l.CompletionRatio,
                l.IsCleared,
                l.Progresses.Where(p => p.IsDone).Select(p => p.QuestId).ToList(),
                l.Note))
            .ToList();
    }

    /// <summary>
    /// GET /xp-events：新到舊。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="limit">筆數；超出範圍會被夾到 1–200。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>事件清單。</returns>
    public async Task<List<XpEventDto>> GetXpEventsAsync(Guid userId, int limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit, 1, MaxEventLimit);
        var events = await db.XpEvents.AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Seq)
            .Take(take)
            .ToListAsync(ct);
        return events.Select(e => new XpEventDto(e.Amount, e.Source, e.RefId, e.OccurredAt.ToUnixSeconds())).ToList();
    }
}
