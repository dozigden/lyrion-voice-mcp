using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LyrionVoiceMcp.Ef.Migrations
{
    /// <inheritdoc />
    public partial class MakeRatingsSearchable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Rating",
                table: "SearchObservations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RatingMatch",
                table: "SearchObservations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Rating",
                table: "SearchObservationCandidates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Rating",
                table: "CatalogueTrackStatistics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CatalogueTrackStatistics_Rating",
                table: "CatalogueTrackStatistics",
                sql: "Rating BETWEEN 0 AND 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CatalogueTrackStatistics_Rating",
                table: "CatalogueTrackStatistics");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "SearchObservations");

            migrationBuilder.DropColumn(
                name: "RatingMatch",
                table: "SearchObservations");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "SearchObservationCandidates");

            migrationBuilder.AlterColumn<int>(
                name: "Rating",
                table: "CatalogueTrackStatistics",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");
        }
    }
}
