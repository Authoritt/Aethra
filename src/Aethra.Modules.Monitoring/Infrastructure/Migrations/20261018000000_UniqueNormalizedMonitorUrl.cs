using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Monitoring.Infrastructure.Migrations;

[DbContext(typeof(MonitoringDbContext))]
[Migration("20261018000000_UniqueNormalizedMonitorUrl")]
public partial class UniqueNormalizedMonitorUrl : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "normalized_url",
            schema: "monitoring",
            table: "monitors",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "normalized_url_hash",
            schema: "monitoring",
            table: "monitors",
            type: "character(64)",
            fixedLength: true,
            maxLength: 64,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ux_monitors_normalized_url",
            schema: "monitoring",
            table: "monitors",
            column: "normalized_url_hash",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_monitors_normalized_url",
            schema: "monitoring",
            table: "monitors");

        migrationBuilder.DropColumn(
            name: "normalized_url_hash",
            schema: "monitoring",
            table: "monitors");

        migrationBuilder.DropColumn(
            name: "normalized_url",
            schema: "monitoring",
            table: "monitors");
    }
}
