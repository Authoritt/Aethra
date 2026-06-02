using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Vms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VmInstallationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "install_log",
                schema: "vms",
                table: "vms",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "install_status",
                schema: "vms",
                table: "vms",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "NotInstalled");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_seen_at",
                schema: "vms",
                table: "vms",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ssh_credentials_cipher",
                schema: "vms",
                table: "vms",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "install_log",
                schema: "vms",
                table: "vms");

            migrationBuilder.DropColumn(
                name: "install_status",
                schema: "vms",
                table: "vms");

            migrationBuilder.DropColumn(
                name: "last_seen_at",
                schema: "vms",
                table: "vms");

            migrationBuilder.DropColumn(
                name: "ssh_credentials_cipher",
                schema: "vms",
                table: "vms");
        }
    }
}
