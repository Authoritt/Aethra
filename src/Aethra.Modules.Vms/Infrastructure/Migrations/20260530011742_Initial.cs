using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Vms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "vms");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "vms",
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
                name: "vms",
                schema: "vms",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    public_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    private_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_disconnected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    satellite_token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    satellite_token_rotated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    satellite_agent_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    satellite_last_handshake_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    satellite_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hostname = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    kernel_version = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    cpu_model = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    cpu_cores = table.Column<int>(type: "integer", nullable: true),
                    total_memory_bytes = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vms", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                schema: "vms",
                table: "outbox_messages",
                columns: new[] { "ProcessedAt", "NextAttemptAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "ix_vms_satellite_token_hash",
                schema: "vms",
                table: "vms",
                column: "satellite_token_hash");

            migrationBuilder.CreateIndex(
                name: "ux_vms_slug",
                schema: "vms",
                table: "vms",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "vms");

            migrationBuilder.DropTable(
                name: "vms",
                schema: "vms");
        }
    }
}
