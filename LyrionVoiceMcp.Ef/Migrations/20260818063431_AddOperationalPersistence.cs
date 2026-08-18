using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LyrionVoiceMcp.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    RunAfterUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledJobStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    LastRunAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastEvaluatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledJobStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ErrorLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReportId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Area = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExceptionType = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    StackTrace = table.Column<string>(type: "TEXT", maxLength: 32768, nullable: true),
                    TraceIdentifier = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RequestMethod = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    RequestPath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    JobId = table.Column<int>(type: "INTEGER", nullable: true),
                    ContextJson = table.Column<string>(type: "TEXT", maxLength: 32768, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErrorLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ErrorLogs_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "JobLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobId = table.Column<int>(type: "INTEGER", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    DataJson = table.Column<string>(type: "TEXT", nullable: true),
                    LoggedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobLogs_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ToolCalls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ToolCallId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ToolName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: true),
                    ArgumentsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ArgumentsTruncated = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    ResultTruncated = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    TraceIdentifier = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ErrorLogId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolCalls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ToolCalls_ErrorLogs_ErrorLogId",
                        column: x => x.ErrorLogId,
                        principalTable: "ErrorLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLogs_JobId",
                table: "ErrorLogs",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLogs_OccurredAtUtc_Id",
                table: "ErrorLogs",
                columns: new[] { "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLogs_ReportId",
                table: "ErrorLogs",
                column: "ReportId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLogs_Source_Area",
                table: "ErrorLogs",
                columns: new[] { "Source", "Area" });

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLogs_TraceIdentifier",
                table: "ErrorLogs",
                column: "TraceIdentifier");

            migrationBuilder.CreateIndex(
                name: "IX_JobLogs_JobId_Id",
                table: "JobLogs",
                columns: new[] { "JobId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_CorrelationId",
                table: "Jobs",
                column: "CorrelationId",
                unique: true,
                filter: "\"CorrelationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_CreatedAtUtc_Id",
                table: "Jobs",
                columns: new[] { "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Status_RunAfterUtc_Id",
                table: "Jobs",
                columns: new[] { "Status", "RunAfterUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Type_Status_Id",
                table: "Jobs",
                columns: new[] { "Type", "Status", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_Jobs_ActiveCatalogueRefresh",
                table: "Jobs",
                column: "Type",
                unique: true,
                filter: "\"Type\" = 'catalogue.refresh' AND \"Status\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobStates_Name",
                table: "ScheduledJobStates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToolCalls_ErrorLogId",
                table: "ToolCalls",
                column: "ErrorLogId");

            migrationBuilder.CreateIndex(
                name: "IX_ToolCalls_StartedAtUtc_Id",
                table: "ToolCalls",
                columns: new[] { "StartedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ToolCalls_ToolCallId",
                table: "ToolCalls",
                column: "ToolCallId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToolCalls_ToolName_Status_StartedAtUtc",
                table: "ToolCalls",
                columns: new[] { "ToolName", "Status", "StartedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobLogs");

            migrationBuilder.DropTable(
                name: "ScheduledJobStates");

            migrationBuilder.DropTable(
                name: "ToolCalls");

            migrationBuilder.DropTable(
                name: "ErrorLogs");

            migrationBuilder.DropTable(
                name: "Jobs");
        }
    }
}
