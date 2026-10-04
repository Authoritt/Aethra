using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Cloudflare.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CloudflareTunnelMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "mode",
                schema: "cloudflare",
                table: "tunnels",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "central_proxy");

            migrationBuilder.CreateIndex(
                name: "ux_tunnels_direct_target_vm_id",
                schema: "cloudflare",
                table: "tunnels",
                column: "target_vm_id",
                unique: true,
                filter: "mode = 'direct'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_tunnels_direct_target_vm_id",
                schema: "cloudflare",
                table: "tunnels");

            migrationBuilder.DropColumn(
                name: "mode",
                schema: "cloudflare",
                table: "tunnels");
        }
    }
}
