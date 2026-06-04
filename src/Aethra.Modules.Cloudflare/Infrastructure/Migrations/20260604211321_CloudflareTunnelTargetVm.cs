using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Cloudflare.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CloudflareTunnelTargetVm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "target_vm_id",
                schema: "cloudflare",
                table: "tunnels",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "target_vm_id",
                schema: "cloudflare",
                table: "tunnels");
        }
    }
}
