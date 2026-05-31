using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Shared.Infrastructure.Persistence.SharedMigrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "shared");

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "shared",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestType = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ResponseJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_keys", x => new { x.Key, x.RequestType });
                });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_expires",
                schema: "shared",
                table: "idempotency_keys",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "shared");
        }
    }
}
