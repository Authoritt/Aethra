using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Proxy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RouteOperationalOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "operational_owner_id",
                schema: "proxy",
                table: "routes",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operational_owner_type",
                schema: "proxy",
                table: "routes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origin",
                schema: "proxy",
                table: "routes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_routes_operational_owner",
                schema: "proxy",
                table: "routes",
                columns: new[] { "operational_owner_type", "operational_owner_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_routes_operational_owner",
                schema: "proxy",
                table: "routes");

            migrationBuilder.DropColumn(
                name: "operational_owner_id",
                schema: "proxy",
                table: "routes");

            migrationBuilder.DropColumn(
                name: "operational_owner_type",
                schema: "proxy",
                table: "routes");

            migrationBuilder.DropColumn(
                name: "origin",
                schema: "proxy",
                table: "routes");
        }
    }
}
