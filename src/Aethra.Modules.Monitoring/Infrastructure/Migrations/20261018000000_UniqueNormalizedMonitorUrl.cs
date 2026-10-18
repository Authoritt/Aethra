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

        migrationBuilder.AddColumn<string>(
            name: "normalized_url_hash",
            schema: "monitoring",
            table: "monitors",
            type: "character(64)",
            fixedLength: true,
            maxLength: 64,
            nullable: true);

        migrationBuilder.Sql(
            """
            WITH parsed AS (
                SELECT id, regexp_match(
                    rtrim(btrim(url), '/'),
                    '^([^:]+)://((?:[^/?#]*@)?)(\[[^]]+\]|[^:/?#]+)(.*)$') AS parts
                FROM monitoring.monitors
            )
            UPDATE monitoring.monitors m
            SET normalized_url =
                translate(parts[1], 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz') ||
                '://' || parts[2] ||
                lower(parts[3]) ||
                parts[4]
            FROM parsed
            WHERE parsed.id = m.id;
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

        migrationBuilder.Sql(
            """
            UPDATE monitoring.monitors
            SET normalized_url_hash = encode(sha256(convert_to(normalized_url, 'UTF8')), 'hex');
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

        migrationBuilder.AlterColumn<string>(
            name: "normalized_url_hash",
            schema: "monitoring",
            table: "monitors",
            type: "character(64)",
            fixedLength: true,
            maxLength: 64,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character(64)",
            oldFixedLength: true,
            oldMaxLength: 64,
            oldNullable: true);

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
