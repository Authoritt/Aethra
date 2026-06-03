using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Services.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class F121_ScheduledJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scheduled_jobs",
                schema: "services",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    service_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    command = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    cron_expression = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    max_concurrent = table.Column<int>(type: "integer", nullable: false),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scheduled_jobs", x => x.id);
                    table.ForeignKey(
                        name: "FK_scheduled_jobs_managed_services_service_id",
                        column: x => x.service_id,
                        principalSchema: "services",
                        principalTable: "managed_services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scheduled_job_runs",
                schema: "services",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    job_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    exit_code = table.Column<int>(type: "integer", nullable: true),
                    stdout = table.Column<string>(type: "text", nullable: true),
                    stderr = table.Column<string>(type: "text", nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scheduled_job_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_scheduled_job_runs_scheduled_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "services",
                        principalTable: "scheduled_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_job_runs_job_time",
                schema: "services",
                table: "scheduled_job_runs",
                columns: new[] { "job_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_jobs_due",
                schema: "services",
                table: "scheduled_jobs",
                columns: new[] { "enabled", "next_run_at" });

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_jobs_service",
                schema: "services",
                table: "scheduled_jobs",
                column: "service_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scheduled_job_runs",
                schema: "services");

            migrationBuilder.DropTable(
                name: "scheduled_jobs",
                schema: "services");
        }
    }
}
