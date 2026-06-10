using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Vms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VmCapabilitySnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "container_runtime",
                schema: "vms",
                table: "vms",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "root_disk_available_bytes",
                schema: "vms",
                table: "vms",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "root_disk_total_bytes",
                schema: "vms",
                table: "vms",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "container_runtime",
                schema: "vms",
                table: "vms");

            migrationBuilder.DropColumn(
                name: "root_disk_available_bytes",
                schema: "vms",
                table: "vms");

            migrationBuilder.DropColumn(
                name: "root_disk_total_bytes",
                schema: "vms",
                table: "vms");
        }
    }
}
