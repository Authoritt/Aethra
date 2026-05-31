using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Projects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEnvVarSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source",
                schema: "projects",
                table: "env_vars",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_env_vars_scope_source",
                schema: "projects",
                table: "env_vars",
                columns: new[] { "scope_type", "scope_id", "source" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_env_vars_scope_source",
                schema: "projects",
                table: "env_vars");

            migrationBuilder.DropColumn(
                name: "source",
                schema: "projects",
                table: "env_vars");
        }
    }
}
