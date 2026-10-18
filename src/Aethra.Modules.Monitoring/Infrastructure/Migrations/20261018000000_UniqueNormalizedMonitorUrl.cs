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
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE monitoring.monitors
            SET normalized_url = lower(rtrim(btrim(url), '/'));
            """);

        migrationBuilder.AlterColumn<string>(
            name: "normalized_url",
            schema: "monitoring",
            table: "monitors",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(2048)",
            oldMaxLength: 2048,
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "ux_monitors_normalized_url",
            schema: "monitoring",
            table: "monitors",
            column: "normalized_url",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_monitors_normalized_url",
            schema: "monitoring",
            table: "monitors");

        migrationBuilder.DropColumn(
            name: "normalized_url",
            schema: "monitoring",
            table: "monitors");
    }
}
