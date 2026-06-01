using System;
using Microsoft.EntityFrameworkCore.Migrations;

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
                    is_literal = table.Column<bool>(type: "boolean", nullable: false),
                    is_multiline = table.Column<bool>(type: "boolean", nullable: false),
                    source = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
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
                    color = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    icon = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "clients",
                schema: "projects",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    project_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    billing_tag = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clients", x => x.id);
                    table.ForeignKey(
                        name: "FK_clients_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "projects",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "templates",
                schema: "projects",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    project_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    source_git_repo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    source_branch = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    source_base_directory = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    source_watch_paths = table.Column<string>(type: "jsonb", nullable: false),
                    source_access_token_credential_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    build_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    build_dockerfile_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    build_compose_file_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    build_args = table.Column<string>(type: "jsonb", nullable: false),
                    webhook_secret = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_templates", x => x.id);
                    table.ForeignKey(
                        name: "FK_templates_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "projects",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "instances",
                schema: "projects",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    template_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    client_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    environment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_vm_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    container_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    hc_test = table.Column<string>(type: "jsonb", nullable: true),
                    hc_interval_seconds = table.Column<int>(type: "integer", nullable: true),
                    hc_retries = table.Column<int>(type: "integer", nullable: true),
                    hc_timeout_seconds = table.Column<int>(type: "integer", nullable: true),
                    hc_start_period_seconds = table.Column<int>(type: "integer", nullable: true),
                    auto_deploy_on_new_build = table.Column<bool>(type: "boolean", nullable: false),
                    custom_domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    auto_hostname = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instances", x => x.id);
                    table.ForeignKey(
                        name: "FK_instances_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "projects",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_instances_templates_template_id",
                        column: x => x.template_id,
                        principalSchema: "projects",
                        principalTable: "templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "instance_ports",
                schema: "projects",
                columns: table => new
                {
                    container_port = table.Column<int>(type: "integer", nullable: false),
                    instance_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    host_port = table.Column<int>(type: "integer", nullable: true),
                    protocol = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instance_ports", x => new { x.instance_id, x.container_port });
                    table.ForeignKey(
                        name: "FK_instance_ports_instances_instance_id",
                        column: x => x.instance_id,
                        principalSchema: "projects",
                        principalTable: "instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "instance_volumes",
                schema: "projects",
                columns: table => new
                {
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    instance_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    container_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    read_only = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instance_volumes", x => new { x.instance_id, x.name });
                    table.ForeignKey(
                        name: "FK_instance_volumes_instances_instance_id",
                        column: x => x.instance_id,
                        principalSchema: "projects",
                        principalTable: "instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_clients_project_id",
                schema: "projects",
                table: "clients",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ux_clients_project_slug",
                schema: "projects",
                table: "clients",
                columns: new[] { "project_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_env_vars_scope_source",
                schema: "projects",
                table: "env_vars",
                columns: new[] { "scope_type", "scope_id", "source" });

            migrationBuilder.CreateIndex(
                name: "ux_env_vars_scope_key",
                schema: "projects",
                table: "env_vars",
                columns: new[] { "scope_type", "scope_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_instances_client_id",
                schema: "projects",
                table: "instances",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_instances_template_id",
                schema: "projects",
                table: "instances",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ux_instances_template_slug",
                schema: "projects",
                table: "instances",
                columns: new[] { "template_id", "slug" },
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

            migrationBuilder.CreateIndex(
                name: "ix_templates_project_id",
                schema: "projects",
                table: "templates",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ux_templates_project_slug",
                schema: "projects",
                table: "templates",
                columns: new[] { "project_id", "slug" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "env_vars",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "instance_ports",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "instance_volumes",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "instances",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "clients",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "templates",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "projects",
                schema: "projects");
        }
    }
}
