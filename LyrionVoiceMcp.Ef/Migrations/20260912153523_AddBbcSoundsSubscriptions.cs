using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LyrionVoiceMcp.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddBbcSoundsSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProviderId",
                table: "SearchObservationCandidates",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BbcSubscribedShows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SnapshotId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ProgrammeId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BbcSubscribedShows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BbcSubscriptionState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    SnapshotId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Available = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BbcSubscriptionState", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BbcSubscribedShows_SnapshotId_Id",
                table: "BbcSubscribedShows",
                columns: new[] { "SnapshotId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_BbcSubscribedShows_SnapshotId_ProgrammeId",
                table: "BbcSubscribedShows",
                columns: new[] { "SnapshotId", "ProgrammeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BbcSubscribedShows");

            migrationBuilder.DropTable(
                name: "BbcSubscriptionState");

            migrationBuilder.DropColumn(
                name: "ProviderId",
                table: "SearchObservationCandidates");
        }
    }
}
