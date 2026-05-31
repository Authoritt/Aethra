using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Notes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notes");

            migrationBuilder.CreateTable(
                name: "notes",
                schema: "notes",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scope_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    markdown_body = table.Column<string>(type: "text", nullable: false),
                    is_pinned = table.Column<bool>(type: "boolean", nullable: false),
                    author_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "notes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pinned_facts",
                schema: "notes",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scope_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value_cipher = table.Column<byte[]>(type: "bytea", nullable: false),
                    is_secret = table.Column<bool>(type: "boolean", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pinned_facts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "note_images",
                schema: "notes",
                columns: table => new
                {
                    image_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    original_filename = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    stored_filename = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_note_images", x => new { x.note_id, x.image_id });
                    table.ForeignKey(
                        name: "FK_note_images_notes_note_id",
                        column: x => x.note_id,
                        principalSchema: "notes",
                        principalTable: "notes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_note_images_image_id",
                schema: "notes",
                table: "note_images",
                column: "image_id");

            migrationBuilder.CreateIndex(
                name: "ix_notes_scope",
                schema: "notes",
                table: "notes",
                columns: new[] { "scope_type", "scope_id" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                schema: "notes",
                table: "outbox_messages",
                columns: new[] { "ProcessedAt", "NextAttemptAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "ux_pinned_facts_scope_key",
                schema: "notes",
                table: "pinned_facts",
                columns: new[] { "scope_type", "scope_id", "key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "note_images",
                schema: "notes");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "notes");

            migrationBuilder.DropTable(
                name: "pinned_facts",
                schema: "notes");

            migrationBuilder.DropTable(
                name: "notes",
                schema: "notes");
        }
    }
}
