using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Proxy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "proxy");

            migrationBuilder.CreateTable(
                name: "certificates",
                schema: "proxy",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hostname = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    pfx_cipher_text = table.Column<string>(type: "text", nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    not_before = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    not_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    renew_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_certificates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "proxy",
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
                name: "routes",
                schema: "proxy",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hostname = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    backend_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    tls_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    certificate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tls_account",
                schema: "proxy",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    account_key_pem = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    use_staging = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tls_account", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_certificates_renew_after",
                schema: "proxy",
                table: "certificates",
                column: "renew_after");

            migrationBuilder.CreateIndex(
                name: "ux_certificates_hostname",
                schema: "proxy",
                table: "certificates",
                column: "hostname",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                schema: "proxy",
                table: "outbox_messages",
                columns: new[] { "ProcessedAt", "NextAttemptAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "ux_routes_hostname",
                schema: "proxy",
                table: "routes",
                column: "hostname",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "certificates",
                schema: "proxy");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "proxy");

            migrationBuilder.DropTable(
                name: "routes",
                schema: "proxy");

            migrationBuilder.DropTable(
                name: "tls_account",
                schema: "proxy");
        }
    }
}
