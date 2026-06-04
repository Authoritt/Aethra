using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Projects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TemplateServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "services",
                schema: "projects",
                table: "templates",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "services",
                schema: "projects",
                table: "templates");
        }
    }
}
