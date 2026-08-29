using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LyrionVoiceMcp.Ef.Migrations
{
    /// <inheritdoc />
    public partial class RecordSearchCandidateMatchSignal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MatchSignal",
                table: "SearchObservationCandidates",
                type: "TEXT",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MatchSignal",
                table: "SearchObservationCandidates");
        }
    }
}
