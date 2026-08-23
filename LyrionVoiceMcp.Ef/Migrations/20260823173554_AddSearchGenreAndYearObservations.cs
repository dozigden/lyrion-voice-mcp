using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LyrionVoiceMcp.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchGenreAndYearObservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EffectiveFromYear",
                table: "SearchObservations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EffectiveToYear",
                table: "SearchObservations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Genre",
                table: "SearchObservations",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestedFromYear",
                table: "SearchObservations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestedToYear",
                table: "SearchObservations",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EffectiveFromYear",
                table: "SearchObservations");

            migrationBuilder.DropColumn(
                name: "EffectiveToYear",
                table: "SearchObservations");

            migrationBuilder.DropColumn(
                name: "Genre",
                table: "SearchObservations");

            migrationBuilder.DropColumn(
                name: "RequestedFromYear",
                table: "SearchObservations");

            migrationBuilder.DropColumn(
                name: "RequestedToYear",
                table: "SearchObservations");
        }
    }
}
