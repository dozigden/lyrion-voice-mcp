using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LyrionVoiceMcp.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledJobConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledJobConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CronExpression = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledJobConfigurations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobConfigurations_Name",
                table: "ScheduledJobConfigurations",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledJobConfigurations");
        }
    }
}
