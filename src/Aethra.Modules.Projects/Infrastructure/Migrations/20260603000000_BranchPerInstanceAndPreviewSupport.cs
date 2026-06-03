using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Projects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BranchPerInstanceAndPreviewSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // -----------------------------------------------------------------
            // templates: rename source_branch → default_branch + flag preview.
            // -----------------------------------------------------------------
            migrationBuilder.RenameColumn(
                name: "source_branch",
                schema: "projects",
                table: "templates",
                newName: "default_branch");

            migrationBuilder.AddColumn<bool>(
                name: "auto_preview_pull_requests",
                schema: "projects",
                table: "templates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // -----------------------------------------------------------------
            // templates_environment_mappings: branch-per-environment hereditario.
            // -----------------------------------------------------------------
            migrationBuilder.CreateTable(
                name: "templates_environment_mappings",
                schema: "projects",
                columns: table => new
                {
                    template_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    environment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    branch = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_templates_environment_mappings", x => new { x.template_id, x.environment });
                    table.ForeignKey(
                        name: "FK_templates_environment_mappings_templates_template_id",
                        column: x => x.template_id,
                        principalSchema: "projects",
                        principalTable: "templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // -----------------------------------------------------------------
            // instances: TrackedRef + IsEphemeral + ExpiresAt + CreatedByUserId.
            // -----------------------------------------------------------------
            migrationBuilder.AddColumn<string>(
                name: "tracked_ref",
                schema: "projects",
                table: "instances",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_ephemeral",
                schema: "projects",
                table: "instances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                schema: "projects",
                table: "instances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_user_id",
                schema: "projects",
                table: "instances",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_instances_is_ephemeral",
                schema: "projects",
                table: "instances",
                column: "is_ephemeral");

            migrationBuilder.CreateIndex(
                name: "ix_instances_tracked_ref",
                schema: "projects",
                table: "instances",
                column: "tracked_ref");

            // -----------------------------------------------------------------
            // projects: PreviewMaxConcurrent + PreviewClientId.
            // -----------------------------------------------------------------
            migrationBuilder.AddColumn<int>(
                name: "preview_max_concurrent",
                schema: "projects",
                table: "projects",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<string>(
                name: "preview_client_id",
                schema: "projects",
                table: "projects",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // -----------------------------------------------------------------
            // Data migration: backfill instances.tracked_ref con refs/heads/{template.default_branch}
            // para preservar el comportamiento previo (todas las Instances trackean la rama del Template).
            // -----------------------------------------------------------------
            migrationBuilder.Sql(@"
                UPDATE projects.instances i
                SET tracked_ref = 'refs/heads/' || t.default_branch
                FROM projects.templates t
                WHERE i.template_id = t.id
                  AND i.tracked_ref IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "preview_client_id",
                schema: "projects",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "preview_max_concurrent",
                schema: "projects",
                table: "projects");

            migrationBuilder.DropIndex(
                name: "ix_instances_tracked_ref",
                schema: "projects",
                table: "instances");

            migrationBuilder.DropIndex(
                name: "ix_instances_is_ephemeral",
                schema: "projects",
                table: "instances");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                schema: "projects",
                table: "instances");

            migrationBuilder.DropColumn(
                name: "expires_at",
                schema: "projects",
                table: "instances");

            migrationBuilder.DropColumn(
                name: "is_ephemeral",
                schema: "projects",
                table: "instances");

            migrationBuilder.DropColumn(
                name: "tracked_ref",
                schema: "projects",
                table: "instances");

            migrationBuilder.DropTable(
                name: "templates_environment_mappings",
                schema: "projects");

            migrationBuilder.DropColumn(
                name: "auto_preview_pull_requests",
                schema: "projects",
                table: "templates");

            migrationBuilder.RenameColumn(
                name: "default_branch",
                schema: "projects",
                table: "templates",
                newName: "source_branch");
        }
    }
}
