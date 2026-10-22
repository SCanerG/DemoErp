using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Demo.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SecureWorkflowProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CancelRequested",
                table: "AiAgentRuns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "AiAgentRuns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RetryOf",
                table: "AiAgentRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedAt",
                table: "AiAgentRuns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiOrderProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Payload = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ResultStatus = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    ErrorCategory = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiOrderProposals", x => x.Id);
                    table.CheckConstraint("CK_AiOrderProposals_Kind", "\"Kind\" IN ('sales','purchase')");
                    table.CheckConstraint("CK_AiOrderProposals_State", "\"State\" IN ('pending','approved','cancelled','expired','stale')");
                    table.ForeignKey(
                        name: "FK_AiOrderProposals_AiExecutions_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "AiExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AiOrderProposals_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiOrderProposals_ExecutionId_CreatedAt",
                table: "AiOrderProposals",
                columns: new[] { "ExecutionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiOrderProposals_UserId_IdempotencyKey",
                table: "AiOrderProposals",
                columns: new[] { "UserId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiOrderProposals");

            migrationBuilder.DropColumn(
                name: "CancelRequested",
                table: "AiAgentRuns");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "AiAgentRuns");

            migrationBuilder.DropColumn(
                name: "RetryOf",
                table: "AiAgentRuns");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "AiAgentRuns");
        }
    }
}
