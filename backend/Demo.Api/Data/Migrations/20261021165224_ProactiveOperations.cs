using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Demo.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProactiveOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MonitoringRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Settings = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    NextRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastEventAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextEventAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoringRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MonitoringRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Trigger = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Identity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Settings = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Lease = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    Findings = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Diagnostics = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoringRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonitoringRuns_MonitoringRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "MonitoringRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OperationalAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Category = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Label = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Facts = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ConditionActive = table.Column<bool>(type: "boolean", nullable: false),
                    Occurrence = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperationalAlerts_MonitoringRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "MonitoringRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MonitoringReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AlertId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Occurrence = table.Column<int>(type: "integer", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoringReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonitoringReviews_AiExecutions_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "AiExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MonitoringReviews_OperationalAlerts_AlertId",
                        column: x => x.AlertId,
                        principalTable: "OperationalAlerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OperationalAlertHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AlertId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Identity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalAlertHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperationalAlertHistory_OperationalAlerts_AlertId",
                        column: x => x.AlertId,
                        principalTable: "OperationalAlerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringReviews_AlertId_UserId_Occurrence_Purpose",
                table: "MonitoringReviews",
                columns: new[] { "AlertId", "UserId", "Occurrence", "Purpose" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringReviews_ExecutionId",
                table: "MonitoringReviews",
                column: "ExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringRules_Kind",
                table: "MonitoringRules",
                column: "Kind",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringRuns_RuleId",
                table: "MonitoringRuns",
                column: "RuleId",
                unique: true,
                filter: "\"State\" IN ('queued','running','retry')");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringRuns_RuleId_RequestKey",
                table: "MonitoringRuns",
                columns: new[] { "RuleId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringRuns_State_AvailableAt",
                table: "MonitoringRuns",
                columns: new[] { "State", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalAlertHistory_AlertId_CreatedAt",
                table: "OperationalAlertHistory",
                columns: new[] { "AlertId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalAlerts_OwnerId_Status_LastSeenAt",
                table: "OperationalAlerts",
                columns: new[] { "OwnerId", "Status", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalAlerts_RuleId_Key",
                table: "OperationalAlerts",
                columns: new[] { "RuleId", "Key" },
                unique: true);
            // Only monitoring configuration is initialized; ERP rows and stock thresholds are untouched.
            migrationBuilder.Sql("""
                INSERT INTO "MonitoringRules" ("Id","Kind","Settings","Enabled","Version","NextRunAt","LastEventAt","NextEventAt")
                SELECT id::uuid, kind, '{"enabled":false,"eventsEnabled":false,"intervalMinutes":60,"periodDays":30,"minimumSamples":5,"percentageChange":50,"absoluteChange":100,"stalledDays":7,"approachingDays":3,"warningAlerts":true}', false, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                FROM (VALUES ('11600000-0000-0000-0000-000000000001','inventory'),('11600000-0000-0000-0000-000000000002','orders'),('11600000-0000-0000-0000-000000000003','sales')) AS defaults(id,kind);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonitoringReviews");

            migrationBuilder.DropTable(
                name: "MonitoringRuns");

            migrationBuilder.DropTable(
                name: "OperationalAlertHistory");

            migrationBuilder.DropTable(
                name: "OperationalAlerts");

            migrationBuilder.DropTable(
                name: "MonitoringRules");
        }
    }
}
