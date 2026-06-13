using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Vms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VmCapabilityDriftReconcile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "data_volume_available_bytes",
                schema: "vms",
                table: "vms",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "data_volume_path",
                schema: "vms",
                table: "vms",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "data_volume_total_bytes",
                schema: "vms",
                table: "vms",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "runtime_socket_accessible",
                schema: "vms",
                table: "vms",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "data_volume_available_bytes",
                schema: "vms",
                table: "vms");

            migrationBuilder.DropColumn(
                name: "data_volume_path",
                schema: "vms",
                table: "vms");

            migrationBuilder.DropColumn(
                name: "data_volume_total_bytes",
                schema: "vms",
                table: "vms");

            migrationBuilder.DropColumn(
                name: "runtime_socket_accessible",
                schema: "vms",
                table: "vms");
        }
    }
}
