using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoloLeveling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGoalsAndProgression : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DaysPerStep",
                table: "Quests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EndValue",
                table: "Quests",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GoalId",
                table: "Quests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StageCount",
                table: "Quests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StartValue",
                table: "Quests",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StepValue",
                table: "Quests",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValueKind",
                table: "Quests",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetSnapshot",
                table: "QuestProgresses",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Goals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Answers = table.Column<string>(type: "jsonb", nullable: false),
                    LengthDays = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Goals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Goals_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Quests_GoalId",
                table: "Quests",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_Goals_UserId_Category",
                table: "Goals",
                columns: new[] { "UserId", "Category" },
                unique: true,
                filter: "\"IsArchived\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Goals_UserId_IsArchived",
                table: "Goals",
                columns: new[] { "UserId", "IsArchived" });

            migrationBuilder.AddForeignKey(
                name: "FK_Quests_Goals_GoalId",
                table: "Quests",
                column: "GoalId",
                principalTable: "Goals",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Quests_Goals_GoalId",
                table: "Quests");

            migrationBuilder.DropTable(
                name: "Goals");

            migrationBuilder.DropIndex(
                name: "IX_Quests_GoalId",
                table: "Quests");

            migrationBuilder.DropColumn(
                name: "DaysPerStep",
                table: "Quests");

            migrationBuilder.DropColumn(
                name: "EndValue",
                table: "Quests");

            migrationBuilder.DropColumn(
                name: "GoalId",
                table: "Quests");

            migrationBuilder.DropColumn(
                name: "StageCount",
                table: "Quests");

            migrationBuilder.DropColumn(
                name: "StartValue",
                table: "Quests");

            migrationBuilder.DropColumn(
                name: "StepValue",
                table: "Quests");

            migrationBuilder.DropColumn(
                name: "ValueKind",
                table: "Quests");

            migrationBuilder.DropColumn(
                name: "TargetSnapshot",
                table: "QuestProgresses");
        }
    }
}
