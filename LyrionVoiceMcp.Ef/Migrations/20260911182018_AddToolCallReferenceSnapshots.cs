using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LyrionVoiceMcp.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddToolCallReferenceSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReferenceSnapshotsJson",
                table: "ToolCalls",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReferenceSnapshotsTruncated",
                table: "ToolCalls",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReferenceSnapshotsJson",
                table: "ToolCalls");

            migrationBuilder.DropColumn(
                name: "ReferenceSnapshotsTruncated",
                table: "ToolCalls");
        }
    }
}
