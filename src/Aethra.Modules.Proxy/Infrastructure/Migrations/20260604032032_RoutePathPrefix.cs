using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Proxy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RoutePathPrefix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_routes_hostname",
                schema: "proxy",
                table: "routes");

            migrationBuilder.AddColumn<string>(
                name: "path_prefix",
                schema: "proxy",
                table: "routes",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "/");

            migrationBuilder.CreateIndex(
                name: "ux_routes_hostname_path",
                schema: "proxy",
                table: "routes",
                columns: new[] { "hostname", "path_prefix" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_routes_hostname_path",
                schema: "proxy",
                table: "routes");

            migrationBuilder.DropColumn(
                name: "path_prefix",
                schema: "proxy",
                table: "routes");

            migrationBuilder.CreateIndex(
                name: "ux_routes_hostname",
                schema: "proxy",
                table: "routes",
                column: "hostname",
                unique: true);
        }
    }
}
