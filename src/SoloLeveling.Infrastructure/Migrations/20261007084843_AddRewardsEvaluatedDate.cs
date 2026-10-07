using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoloLeveling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRewardsEvaluatedDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "RewardsEvaluatedDate",
                table: "Players",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RewardsEvaluatedDate",
                table: "Players");
        }
    }
}
