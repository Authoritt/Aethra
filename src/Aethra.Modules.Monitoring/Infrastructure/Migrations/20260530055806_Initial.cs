using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Monitoring.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "monitoring");

            migrationBuilder.CreateTable(
                name: "monitor_checks",
                schema: "monitoring",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    monitor_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    http_status_code = table.Column<int>(type: "integer", nullable: true),
                    latency_ms = table.Column<int>(type: "integer", nullable: true),
                    error_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    response_snippet = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monitor_checks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "monitors",
                schema: "monitoring",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    http_method = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    interval_sec = table.Column<int>(type: "integer", nullable: false),
                    timeout_ms = table.Column<int>(type: "integer", nullable: false),
                    body_template = table.Column<string>(type: "text", nullable: true),
                    instance_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    project_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    expected_status_codes = table.Column<string>(type: "jsonb", nullable: false),
                    headers = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monitors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_monitor_checks_monitor_time",
                schema: "monitoring",
                table: "monitor_checks",
                columns: new[] { "monitor_id", "timestamp" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_monitors_instance",
                schema: "monitoring",
                table: "monitors",
                column: "instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitors_enabled_last_checked",
                schema: "monitoring",
                table: "monitors",
                columns: new[] { "is_enabled", "last_checked_at" });

            migrationBuilder.CreateIndex(
                name: "ix_monitors_project",
                schema: "monitoring",
                table: "monitors",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ux_monitors_slug",
                schema: "monitoring",
                table: "monitors",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                schema: "monitoring",
                table: "outbox_messages",
                columns: new[] { "ProcessedAt", "NextAttemptAt", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monitor_checks",
                schema: "monitoring");

            migrationBuilder.DropTable(
                name: "monitors",
                schema: "monitoring");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "monitoring");
        }
    }
}
