using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Demo.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AgentControlCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiAgentRuns",
                columns: table => new
                {
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAgentRuns", x => x.ExecutionId);
                    table.ForeignKey(
                        name: "FK_AiAgentRuns_AiExecutions_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "AiExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiAgentSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Agent = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAgentSteps", x => x.Id);
                    table.CheckConstraint("CK_AiAgentSteps_Sequence", "\"Sequence\" >= 0 AND \"Sequence\" < 5");
                    table.ForeignKey(
                        name: "FK_AiAgentSteps_AiAgentRuns_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "AiAgentRuns",
                        principalColumn: "ExecutionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiAgentSteps_ExecutionId_Sequence",
                table: "AiAgentSteps",
                columns: new[] { "ExecutionId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiAgentSteps");

            migrationBuilder.DropTable(
                name: "AiAgentRuns");
        }
    }
}
