using Microsoft.EntityFrameworkCore;
using SoloLeveling.Api.Contracts;
using SoloLeveling.Infrastructure;
using ProgramEntity = SoloLeveling.Domain.Entities.Program;

namespace SoloLeveling.Api.Services;

/// <summary>
/// 66 天計畫週期。
/// </summary>
/// <param name="db">DbContext。</param>
/// <param name="loader">今日內容載入（含結算）。</param>
/// <param name="clock">時間來源。</param>
public class ProgramService(AppDbContext db, TodayContextLoader loader, TimeProvider clock)
{
    /// <summary>
    /// POST /program/restart：結束目前週期，從今日開新週期（Cycle + 1）；不重置玩家等級與屬性。
    /// </summary>
    /// <param name="userId">使用者 ID。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>新週期。</returns>
    public async Task<ProgramDto> RestartAsync(Guid userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = await loader.LoadAsync(userId, ct);
        var current = await db.Programs.SingleAsync(p => p.UserId == userId && p.IsActive, ct);

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
            CreatedAt = clock.GetUtcNow(),
        };
        db.Programs.Add(next);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return next.ToDto(context.Today);
    }
}
