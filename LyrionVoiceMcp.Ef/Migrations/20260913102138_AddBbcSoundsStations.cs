using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LyrionVoiceMcp.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddBbcSoundsStations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BbcStations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SnapshotId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StationId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    LiveAudioUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BbcStations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BbcStationState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    SnapshotId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Available = table.Column<bool>(type: "INTEGER", nullable: false),
                    StationCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BbcStationState", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BbcStations_SnapshotId_Id",
                table: "BbcStations",
                columns: new[] { "SnapshotId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_BbcStations_SnapshotId_StationId",
                table: "BbcStations",
                columns: new[] { "SnapshotId", "StationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BbcStations");

            migrationBuilder.DropTable(
                name: "BbcStationState");
        }
    }
}
