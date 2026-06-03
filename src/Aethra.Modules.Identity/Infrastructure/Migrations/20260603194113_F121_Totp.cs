using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class F121_Totp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "totp_enabled",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "totp_enabled_at",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "totp_recovery_codes_cipher",
                schema: "identity",
                table: "users",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "totp_recovery_codes_used_mask",
                schema: "identity",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte[]>(
                name: "totp_secret_cipher",
                schema: "identity",
                table: "users",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "totp_enabled",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "totp_enabled_at",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "totp_recovery_codes_cipher",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "totp_recovery_codes_used_mask",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "totp_secret_cipher",
                schema: "identity",
                table: "users");
        }
    }
}
