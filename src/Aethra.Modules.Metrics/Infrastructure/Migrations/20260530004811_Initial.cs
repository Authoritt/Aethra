using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Metrics.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "metrics");

            migrationBuilder.CreateTable(
                name: "container_snapshots",
                schema: "metrics",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    vm_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    container_count = table.Column<int>(type: "integer", nullable: false),
                    containers = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_container_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "metrics",
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
                name: "vm_metrics",
                schema: "metrics",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    vm_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cpu_percent = table.Column<double>(type: "double precision", nullable: false),
                    load_1 = table.Column<double>(type: "double precision", nullable: false),
                    load_5 = table.Column<double>(type: "double precision", nullable: false),
                    load_15 = table.Column<double>(type: "double precision", nullable: false),
                    mem_used = table.Column<long>(type: "bigint", nullable: false),
                    mem_free = table.Column<long>(type: "bigint", nullable: false),
                    mem_total = table.Column<long>(type: "bigint", nullable: false),
                    swap_used = table.Column<long>(type: "bigint", nullable: false),
                    swap_total = table.Column<long>(type: "bigint", nullable: false),
                    disks = table.Column<string>(type: "jsonb", nullable: false),
                    net_bytes_rx = table.Column<long>(type: "bigint", nullable: false),
                    net_bytes_tx = table.Column<long>(type: "bigint", nullable: false),
                    net_packets_rx = table.Column<long>(type: "bigint", nullable: false),
                    net_packets_tx = table.Column<long>(type: "bigint", nullable: false),
                    uptime_seconds = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vm_metrics", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_container_snapshots_vm_time",
                schema: "metrics",
                table: "container_snapshots",
                columns: new[] { "vm_id", "timestamp" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                schema: "metrics",
                table: "outbox_messages",
                columns: new[] { "ProcessedAt", "NextAttemptAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "ix_vm_metrics_vm_time",
                schema: "metrics",
                table: "vm_metrics",
                columns: new[] { "vm_id", "timestamp" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "container_snapshots",
                schema: "metrics");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "metrics");

            migrationBuilder.DropTable(
                name: "vm_metrics",
                schema: "metrics");
        }
    }
}
