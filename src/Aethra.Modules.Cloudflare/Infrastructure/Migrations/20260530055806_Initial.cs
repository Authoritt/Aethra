using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Cloudflare.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cloudflare");

            migrationBuilder.CreateTable(
                name: "dns_records",
                schema: "cloudflare",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    external_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ttl = table.Column<int>(type: "integer", nullable: false),
                    proxied = table.Column<bool>(type: "boolean", nullable: false),
                    comment = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dns_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "cloudflare",
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
                name: "zones",
                schema: "cloudflare",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    external_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    api_token_cipher = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_zones", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dns_records_external_record_id",
                schema: "cloudflare",
                table: "dns_records",
                column: "external_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_dns_records_zone_id",
                schema: "cloudflare",
                table: "dns_records",
                column: "zone_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                schema: "cloudflare",
                table: "outbox_messages",
                columns: new[] { "ProcessedAt", "NextAttemptAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "ix_zones_name",
                schema: "cloudflare",
                table: "zones",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ux_zones_external_zone_id",
                schema: "cloudflare",
                table: "zones",
                column: "external_zone_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dns_records",
                schema: "cloudflare");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "cloudflare");

            migrationBuilder.DropTable(
                name: "zones",
                schema: "cloudflare");
        }
    }
}
