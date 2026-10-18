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

        // Una instalacion que ya sufrio el bug (#32) puede tener monitores con la misma URL
        // normalizada, y entonces el indice unico no se podria crear y la migracion dejaria la
        // instalacion sin poder actualizar. No se borra ni se apaga ninguno: el mas antiguo de
        // cada grupo conserva la clave y los demas reciben una clave distinguible
        // ('<url>#duplicado-<id>') que el operador resuelve editandolos. Al editar, la clave se
        // recalcula y la regla nueva le pide cambiar la URL. 1970 + sufijo (11) + id (<=64) <= 2048.
        migrationBuilder.Sql(
            """
            UPDATE monitoring.monitors m
            SET normalized_url = left(m.normalized_url, 1970) || '#duplicado-' || m.id
            FROM (
                SELECT id, row_number() OVER (PARTITION BY normalized_url ORDER BY created_at, id) AS rn
                FROM monitoring.monitors
            ) d
            WHERE d.id = m.id AND d.rn > 1;
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
