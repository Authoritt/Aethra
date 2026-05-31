using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aethra.Modules.Projects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "projects");

            migrationBuilder.CreateTable(
                name: "env_vars",
                schema: "projects",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scope_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    is_build_time = table.Column<bool>(type: "boolean", nullable: false),
                    is_runtime = table.Column<bool>(type: "boolean", nullable: false),
                    is_secret = table.Column<bool>(type: "boolean", nullable: false),
                    is_literal = table.Column<bool>(type: "boolean", nullable: false),
                    is_multiline = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_env_vars", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "projects",
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
                name: "projects",
                schema: "projects",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    color = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    icon = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "environments",
                schema: "projects",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    project_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_environments", x => x.id);
                    table.ForeignKey(
                        name: "FK_environments_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "projects",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "applications",
                schema: "projects",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_git_repo_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    source_branch = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_webhook_secret = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_base_directory = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    source_watch_paths = table.Column<string[]>(type: "text[]", nullable: false),
                    source_access_token_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    build_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    build_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    runtime_target_vm_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    runtime_container_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    hc_cmd = table.Column<string[]>(type: "text[]", nullable: true),
                    hc_interval = table.Column<TimeSpan>(type: "interval", nullable: true),
                    hc_timeout = table.Column<TimeSpan>(type: "interval", nullable: true),
                    hc_retries = table.Column<int>(type: "integer", nullable: true),
                    hc_start_period = table.Column<TimeSpan>(type: "interval", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_applications", x => x.id);
                    table.ForeignKey(
                        name: "FK_applications_environments_environment_id",
                        column: x => x.environment_id,
                        principalSchema: "projects",
                        principalTable: "environments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "application_build_args",
                schema: "projects",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    application_id = table.Column<string>(type: "character varying(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_build_args", x => x.id);
                    table.ForeignKey(
                        name: "FK_application_build_args_applications_application_id",
                        column: x => x.application_id,
                        principalSchema: "projects",
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "application_runtime_ports",
                schema: "projects",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    container_port = table.Column<int>(type: "integer", nullable: false),
                    host_port = table.Column<int>(type: "integer", nullable: true),
                    protocol = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    application_id = table.Column<string>(type: "character varying(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_runtime_ports", x => x.id);
                    table.ForeignKey(
                        name: "FK_application_runtime_ports_applications_application_id",
                        column: x => x.application_id,
                        principalSchema: "projects",
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "application_runtime_volumes",
                schema: "projects",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    host_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    container_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    read_only = table.Column<bool>(type: "boolean", nullable: false),
                    application_id = table.Column<string>(type: "character varying(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_runtime_volumes", x => x.id);
                    table.ForeignKey(
                        name: "FK_application_runtime_volumes_applications_application_id",
                        column: x => x.application_id,
                        principalSchema: "projects",
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_application_build_args_application_id",
                schema: "projects",
                table: "application_build_args",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "IX_application_runtime_ports_application_id",
                schema: "projects",
                table: "application_runtime_ports",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "IX_application_runtime_volumes_application_id",
                schema: "projects",
                table: "application_runtime_volumes",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ux_applications_env_slug",
                schema: "projects",
                table: "applications",
                columns: new[] { "environment_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_env_vars_scope_key",
                schema: "projects",
                table: "env_vars",
                columns: new[] { "scope_type", "scope_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_environments_project_name",
                schema: "projects",
                table: "environments",
                columns: new[] { "project_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                schema: "projects",
                table: "outbox_messages",
                columns: new[] { "ProcessedAt", "NextAttemptAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "ux_projects_slug",
                schema: "projects",
                table: "projects",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_build_args",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "application_runtime_ports",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "application_runtime_volumes",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "env_vars",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "applications",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "environments",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "projects",
                schema: "projects");
        }
    }
}
