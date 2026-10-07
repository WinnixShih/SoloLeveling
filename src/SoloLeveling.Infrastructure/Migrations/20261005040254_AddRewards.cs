using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SoloLeveling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRewards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CompletedAt",
                table: "Programs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Coins",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PeakLevel",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PinnedCardId",
                table: "Players",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShieldCount",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ThemeKey",
                table: "Players",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "azure");

            migrationBuilder.AddColumn<string>(
                name: "TitlePrefixKey",
                table: "Players",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TitleSuffixKey",
                table: "Players",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CompletedAt",
                table: "Goals",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ClearCoinsGranted",
                table: "DailyLogs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Achievements",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UnlockedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Achievements", x => new { x.UserId, x.Key });
                    table.ForeignKey(
                        name: "FK_Achievements_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoinEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RefId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoinEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoinEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OwnedCards",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    FirstAcquiredAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnedCards", x => new { x.UserId, x.CardId });
                    table.ForeignKey(
                        name: "FK_OwnedCards_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OwnedThemes",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ThemeKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnedThemes", x => new { x.UserId, x.ThemeKey });
                    table.ForeignKey(
                        name: "FK_OwnedThemes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RewardChests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rarity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    OpenedAt = table.Column<long>(type: "bigint", nullable: true),
                    DroppedCardId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Coins = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RewardChests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RewardChests_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RewardEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    OccurredAt = table.Column<long>(type: "bigint", nullable: false),
                    AnnouncedAt = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RewardEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RewardEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoinEvents_UserId_OccurredAt",
                table: "CoinEvents",
                columns: new[] { "UserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RewardChests_UserId_OpenedAt",
                table: "RewardChests",
                columns: new[] { "UserId", "OpenedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RewardEvents_UserId_AnnouncedAt",
                table: "RewardEvents",
                columns: new[] { "UserId", "AnnouncedAt" });

            // 既有玩家不補發歷史升級寶箱；上線前已達標的日子不補發達標金幣
            migrationBuilder.Sql("UPDATE \"Players\" SET \"PeakLevel\" = \"Level\";");
            migrationBuilder.Sql("UPDATE \"DailyLogs\" SET \"ClearCoinsGranted\" = \"BonusGranted\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Achievements");

            migrationBuilder.DropTable(
                name: "CoinEvents");

            migrationBuilder.DropTable(
                name: "OwnedCards");

            migrationBuilder.DropTable(
                name: "OwnedThemes");

            migrationBuilder.DropTable(
                name: "RewardChests");

            migrationBuilder.DropTable(
                name: "RewardEvents");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Programs");

            migrationBuilder.DropColumn(
                name: "Coins",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "PeakLevel",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "PinnedCardId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "ShieldCount",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "ThemeKey",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "TitlePrefixKey",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "TitleSuffixKey",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "ClearCoinsGranted",
                table: "DailyLogs");
        }
    }
}
