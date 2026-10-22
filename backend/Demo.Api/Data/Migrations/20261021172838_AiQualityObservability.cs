using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Demo.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AiQualityObservability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiQualityEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Suite = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DatasetHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Report = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiQualityEvaluations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiQualitySpans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    TraceId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SpanId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ParentSpanId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Operation = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Component = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Error = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Environment = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Tokens = table.Column<int>(type: "integer", nullable: true),
                    ContextCharacters = table.Column<int>(type: "integer", nullable: true),
                    RetrievedCount = table.Column<int>(type: "integer", nullable: true),
                    Retry = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiQualitySpans", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiQualityEvaluations_ExpiresAt",
                table: "AiQualityEvaluations",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AiQualityEvaluations_UserId_CreatedAt",
                table: "AiQualityEvaluations",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiQualityEvaluations_UserId_RequestKey",
                table: "AiQualityEvaluations",
                columns: new[] { "UserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiQualitySpans_ExpiresAt",
                table: "AiQualitySpans",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AiQualitySpans_StartedAt",
                table: "AiQualitySpans",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AiQualitySpans_TraceId_UserId",
                table: "AiQualitySpans",
                columns: new[] { "TraceId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiQualitySpans_UserId_StartedAt",
                table: "AiQualitySpans",
                columns: new[] { "UserId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiQualityEvaluations");

            migrationBuilder.DropTable(
                name: "AiQualitySpans");
        }
    }
}
