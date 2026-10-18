using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Gh86TotpChallengeSingleUse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "totp_challenges",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_totp_challenges", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_totp_challenges_expires_at",
                schema: "identity",
                table: "totp_challenges",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_totp_challenges_user_id",
                schema: "identity",
                table: "totp_challenges",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "totp_challenges",
                schema: "identity");
        }
    }
}
