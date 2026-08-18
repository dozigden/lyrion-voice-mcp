using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LyrionVoiceMcp.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchObservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SearchObservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ObservationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OriginalQuery = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    NormalisedQuery = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    RequestedKind = table.Column<int>(type: "INTEGER", nullable: true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Collection = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Resolver = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ResolverVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureMessage = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    TotalDurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                    RetrievalDurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                    ProcessingDurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchObservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SearchObservationCandidates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SearchObservationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaId = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Artist = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Album = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchObservationCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SearchObservationCandidates_SearchObservations_SearchObservationId",
                        column: x => x.SearchObservationId,
                        principalTable: "SearchObservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SearchObservationRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SearchObservationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Command = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureMessage = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    DurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                    ResultCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchObservationRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SearchObservationRequests_SearchObservations_SearchObservationId",
                        column: x => x.SearchObservationId,
                        principalTable: "SearchObservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SearchObservationReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SearchObservationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Classification = table.Column<int>(type: "INTEGER", nullable: false),
                    ExpectedCorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ExpectedKind = table.Column<int>(type: "INTEGER", nullable: true),
                    ExpectedTitle = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    ExpectedArtist = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    ExpectedAlbum = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: true),
                    IncludeInEvaluation = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchObservationReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SearchObservationReviews_SearchObservations_SearchObservationId",
                        column: x => x.SearchObservationId,
                        principalTable: "SearchObservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SearchObservationSelections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SearchObservationCandidateId = table.Column<int>(type: "INTEGER", nullable: false),
                    SelectedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchObservationSelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SearchObservationSelections_SearchObservationCandidates_SearchObservationCandidateId",
                        column: x => x.SearchObservationCandidateId,
                        principalTable: "SearchObservationCandidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SearchObservationCandidates_CorrelationId",
                table: "SearchObservationCandidates",
                column: "CorrelationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SearchObservationCandidates_SearchObservationId_Position",
                table: "SearchObservationCandidates",
                columns: new[] { "SearchObservationId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SearchObservationRequests_SearchObservationId_Sequence",
                table: "SearchObservationRequests",
                columns: new[] { "SearchObservationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SearchObservationReviews_IncludeInEvaluation",
                table: "SearchObservationReviews",
                column: "IncludeInEvaluation");

            migrationBuilder.CreateIndex(
                name: "IX_SearchObservationReviews_SearchObservationId",
                table: "SearchObservationReviews",
                column: "SearchObservationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SearchObservations_CreatedAtUtc",
                table: "SearchObservations",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SearchObservations_ObservationId",
                table: "SearchObservations",
                column: "ObservationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SearchObservations_Status",
                table: "SearchObservations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SearchObservationSelections_SearchObservationCandidateId",
                table: "SearchObservationSelections",
                column: "SearchObservationCandidateId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SearchObservationSelections_SelectedAtUtc",
                table: "SearchObservationSelections",
                column: "SelectedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SearchObservationRequests");

            migrationBuilder.DropTable(
                name: "SearchObservationReviews");

            migrationBuilder.DropTable(
                name: "SearchObservationSelections");

            migrationBuilder.DropTable(
                name: "SearchObservationCandidates");

            migrationBuilder.DropTable(
                name: "SearchObservations");
        }
    }
}
