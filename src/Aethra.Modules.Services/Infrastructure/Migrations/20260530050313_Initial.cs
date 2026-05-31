using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Services.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "services");

            migrationBuilder.CreateTable(
                name: "managed_services",
                schema: "services",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    target_vm_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    container_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    image = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    internal_port = table.Column<int>(type: "integer", nullable: false),
                    network_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    admin_credentials_cipher = table.Column<byte[]>(type: "bytea", nullable: false),
                    exposed_externally = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    provisioned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managed_services", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "services",
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
                name: "service_bindings",
                schema: "services",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    service_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    instance_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    resource_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    credentials_cipher = table.Column<byte[]>(type: "bytea", nullable: false),
                    permissions = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    injected_env_var_prefix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    migrations_hook_command = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    migrations_hook_timeout_seconds = table.Column<int>(type: "integer", nullable: true),
                    migrations_hook_fail_on_error = table.Column<bool>(type: "boolean", nullable: true),
                    migrations_hook_run_on = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    provisioned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_rotated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_bindings", x => x.id);
                    table.ForeignKey(
                        name: "FK_service_bindings_managed_services_service_id",
                        column: x => x.service_id,
                        principalSchema: "services",
                        principalTable: "managed_services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_managed_services_slug",
                schema: "services",
                table: "managed_services",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                schema: "services",
                table: "outbox_messages",
                columns: new[] { "ProcessedAt", "NextAttemptAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_service_bindings_service_id",
                schema: "services",
                table: "service_bindings",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ux_service_bindings_instance_service_active",
                schema: "services",
                table: "service_bindings",
                columns: new[] { "instance_id", "service_id" },
                unique: true,
                filter: "revoked_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "services");

            migrationBuilder.DropTable(
                name: "service_bindings",
                schema: "services");

            migrationBuilder.DropTable(
                name: "managed_services",
                schema: "services");
        }
    }
}
