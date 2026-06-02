using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Services.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBackups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "backup_cron",
                schema: "services",
                table: "managed_services",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "backup_destination",
                schema: "services",
                table: "managed_services",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "backup_retention",
                schema: "services",
                table: "managed_services",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_backup_at",
                schema: "services",
                table: "managed_services",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_restored_at",
                schema: "services",
                table: "managed_services",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "service_backups",
                schema: "services",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    service_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    destination_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_backups", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_service_backups_service",
                schema: "services",
                table: "service_backups",
                column: "service_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_backups",
                schema: "services");

            migrationBuilder.DropColumn(
                name: "backup_cron",
                schema: "services",
                table: "managed_services");

            migrationBuilder.DropColumn(
                name: "backup_destination",
                schema: "services",
                table: "managed_services");

            migrationBuilder.DropColumn(
                name: "backup_retention",
                schema: "services",
                table: "managed_services");

            migrationBuilder.DropColumn(
                name: "last_backup_at",
                schema: "services",
                table: "managed_services");

            migrationBuilder.DropColumn(
                name: "last_restored_at",
                schema: "services",
                table: "managed_services");
        }
    }
}
