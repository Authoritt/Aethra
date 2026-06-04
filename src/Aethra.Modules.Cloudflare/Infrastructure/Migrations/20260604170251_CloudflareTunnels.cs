using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Cloudflare.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CloudflareTunnels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tunnels",
                schema: "cloudflare",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    external_tunnel_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    account_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    api_token_cipher = table.Column<byte[]>(type: "bytea", nullable: false),
                    aethra_service = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    fallback_service = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    fallback_no_tls_verify = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tunnels", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_tunnels_external_tunnel_id",
                schema: "cloudflare",
                table: "tunnels",
                column: "external_tunnel_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tunnels",
                schema: "cloudflare");
        }
    }
}
