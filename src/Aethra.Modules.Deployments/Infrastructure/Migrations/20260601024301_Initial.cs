using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Deployments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "deployments");

            migrationBuilder.CreateTable(
                name: "builds",
                schema: "deployments",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    template_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    git_sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    git_ref = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    trigger = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    triggered_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    image_ref = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    build_duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    error_message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    failed_at_stage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_builds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "deployments",
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

            migrationBuilder.CreateTable(
                name: "build_logs",
                schema: "deployments",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    build_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    level = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_build_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_build_logs_builds_build_id",
                        column: x => x.build_id,
                        principalSchema: "deployments",
                        principalTable: "builds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_build_logs_build_seq",
                schema: "deployments",
                table: "build_logs",
                columns: new[] { "build_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_builds_status_active",
                schema: "deployments",
                table: "builds",
                column: "status",
                filter: "status IN ('Queued', 'Cloning', 'Building', 'Pushing')");

            migrationBuilder.CreateIndex(
                name: "ix_builds_template_sha",
                schema: "deployments",
                table: "builds",
                columns: new[] { "template_id", "git_sha" });

            migrationBuilder.CreateIndex(
                name: "ix_builds_template_time",
                schema: "deployments",
                table: "builds",
                columns: new[] { "template_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                schema: "deployments",
                table: "outbox_messages",
                columns: new[] { "ProcessedAt", "NextAttemptAt", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "build_logs",
                schema: "deployments");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "deployments");

            migrationBuilder.DropTable(
                name: "builds",
                schema: "deployments");
        }
    }
}
