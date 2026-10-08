using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoloLeveling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceProgramsWithGates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 既有資料改用新的寶箱來源與成就鍵，否則讀取舊寶箱時字串轉 enum 失敗、已選的稱號字塊查不到
            migrationBuilder.Sql("UPDATE \"RewardChests\" SET \"Source\" = 'Gate' WHERE \"Source\" = 'ProgramCompleted';");
            migrationBuilder.Sql("UPDATE \"Achievements\" SET \"Key\" = 'gate-e' WHERE \"Key\" = 'program-complete';");
            migrationBuilder.Sql("UPDATE \"Players\" SET \"TitlePrefixKey\" = 'gate-e' WHERE \"TitlePrefixKey\" = 'program-complete';");

            migrationBuilder.DropTable(
                name: "Programs");

            migrationBuilder.CreateTable(
                name: "Gates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rank = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LengthDays = table.Column<int>(type: "integer", nullable: false),
                    AllowedMisses = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EndedAt = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Gates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Gates_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Gates_UserId",
                table: "Gates",
                column: "UserId",
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_Gates_UserId_Status",
                table: "Gates",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Gates");

            migrationBuilder.CreateTable(
                name: "Programs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompletedAt = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    Cycle = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LengthDays = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Programs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Programs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Programs_UserId",
                table: "Programs",
                column: "UserId",
                unique: true,
                filter: "\"IsActive\" = true");
        }
    }
}
