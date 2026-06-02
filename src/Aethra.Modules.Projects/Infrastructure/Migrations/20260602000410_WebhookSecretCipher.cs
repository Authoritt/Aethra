using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Projects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WebhookSecretCipher : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "webhook_secret",
                schema: "projects",
                table: "templates");

            migrationBuilder.AddColumn<byte[]>(
                name: "webhook_secret_cipher",
                schema: "projects",
                table: "templates",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "webhook_secret_cipher",
                schema: "projects",
                table: "templates");

            migrationBuilder.AddColumn<string>(
                name: "webhook_secret",
                schema: "projects",
                table: "templates",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");
        }
    }
}
