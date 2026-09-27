using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SoloLeveling.Domain.Entities;

namespace SoloLeveling.Infrastructure;

/// <summary>
/// EF Core DbContext。時間戳欄位在此層與 DB 的 <c>bigint</c> Unix 毫秒互轉，Domain 端一律用 <see cref="DateTimeOffset"/>。
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    /// <summary>使用者帳號。</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>玩家狀態。</summary>
    public DbSet<Player> Players => Set<Player>();

    /// <summary>66 天計畫週期。</summary>
    public DbSet<Program> Programs => Set<Program>();

    /// <summary>任務。</summary>
    public DbSet<Quest> Quests => Set<Quest>();

    /// <summary>每日紀錄。</summary>
    public DbSet<DailyLog> DailyLogs => Set<DailyLog>();

    /// <summary>任務每日進度。</summary>
    public DbSet<QuestProgress> QuestProgresses => Set<QuestProgress>();

    /// <summary>EXP 流水。</summary>
    public DbSet<XpEvent> XpEvents => Set<XpEvent>();

    /// <summary>
    /// 設定實體對應：主鍵、唯一鍵、索引、欄位長度與精度，以及時間戳的 Unix 毫秒轉換。
    /// </summary>
    /// <param name="modelBuilder">EF Core 模型建構器。</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");

        modelBuilder.Entity<User>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Email).HasColumnType("citext").IsRequired();
            b.HasIndex(x => x.Email).IsUnique();
            b.Property(x => x.PasswordHash).IsRequired();
            b.Property(x => x.DisplayName).HasMaxLength(40).IsRequired();
            b.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<Player>(b =>
        {
            b.HasKey(x => x.UserId);
            b.HasOne<User>().WithOne().HasForeignKey<Player>(x => x.UserId);
        });

        modelBuilder.Entity<Program>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            // 每位使用者同時只有一筆進行中的週期
            b.HasIndex(x => x.UserId).IsUnique().HasFilter("\"IsActive\" = true");
        });

        modelBuilder.Entity<Quest>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            b.Property(x => x.Name).HasMaxLength(60).IsRequired();
            b.Property(x => x.Unit).HasMaxLength(10);
            b.Property(x => x.StatType).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.Difficulty).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.QuestType).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.TargetValue).HasPrecision(10, 2);
            b.Property(x => x.Step).HasPrecision(10, 2);
            b.HasIndex(x => new { x.UserId, x.IsArchived });
        });

        modelBuilder.Entity<DailyLog>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            b.Property(x => x.CompletionRatio).HasPrecision(5, 4);
            b.HasIndex(x => new { x.UserId, x.Date }).IsUnique();
            b.HasMany(x => x.Progresses).WithOne().HasForeignKey(x => x.DailyLogId);
        });

        modelBuilder.Entity<QuestProgress>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne<Quest>().WithMany().HasForeignKey(x => x.QuestId);
            b.Property(x => x.Value).HasPrecision(10, 2);
            b.HasIndex(x => new { x.DailyLogId, x.QuestId }).IsUnique();
        });

        modelBuilder.Entity<XpEvent>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            b.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
            b.HasIndex(x => new { x.UserId, x.OccurredAt });
        });
    }

    /// <summary>
    /// 全域慣例：所有 <see cref="DateTimeOffset"/> 欄位存為 Unix 毫秒（<c>bigint</c>）。
    /// </summary>
    /// <param name="configurationBuilder">EF Core 慣例建構器。</param>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UnixMillisecondsConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<NullableUnixMillisecondsConverter>();
    }

    /// <summary>
    /// <see cref="DateTimeOffset"/> 與 Unix 毫秒的互轉；是本專案唯一允許出現毫秒換算的地方之一（另一處是 API DTO 的秒換算）。
    /// </summary>
    private sealed class UnixMillisecondsConverter() : ValueConverter<DateTimeOffset, long>(
        v => v.ToUnixTimeMilliseconds(),
        v => DateTimeOffset.FromUnixTimeMilliseconds(v));

    /// <summary>
    /// 可為 null 的 <see cref="DateTimeOffset"/> 與 Unix 毫秒互轉。
    /// </summary>
    private sealed class NullableUnixMillisecondsConverter() : ValueConverter<DateTimeOffset?, long?>(
        v => v.HasValue ? v.Value.ToUnixTimeMilliseconds() : null,
        v => v.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(v.Value) : null);
}
