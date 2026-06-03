using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UserGitHubUsername : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F12.3 — handle de GitHub para que el webhook handler mapee PR.user.login a User Aethra.
            // El index unique tiene WHERE github_username IS NOT NULL (partial) para permitir múltiples
            // users sin handle configurado sin chocar el unique constraint.
            migrationBuilder.AddColumn<string>(
                name: "github_username",
                schema: "identity",
                table: "users",
                type: "character varying(39)",
                maxLength: 39,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_users_github_username",
                schema: "identity",
                table: "users",
                column: "github_username",
                unique: true,
                filter: "\"github_username\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_users_github_username",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "github_username",
                schema: "identity",
                table: "users");
        }
    }
}
