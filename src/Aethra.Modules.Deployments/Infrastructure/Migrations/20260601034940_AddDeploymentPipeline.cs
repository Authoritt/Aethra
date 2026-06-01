using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Deployments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeploymentPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "deployments",
                schema: "deployments",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    build_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    instance_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    trigger = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    triggered_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    old_container_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    new_container_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    old_image_ref = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    new_image_ref = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    error_message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    failed_at_stage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deployments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "deployment_logs",
                schema: "deployments",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    deployment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    level = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deployment_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_deployment_logs_deployments_deployment_id",
                        column: x => x.deployment_id,
                        principalSchema: "deployments",
                        principalTable: "deployments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_deployment_logs_deployment_seq",
                schema: "deployments",
                table: "deployment_logs",
                columns: new[] { "deployment_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_deployments_build",
                schema: "deployments",
                table: "deployments",
                column: "build_id");

            migrationBuilder.CreateIndex(
                name: "ix_deployments_instance_status",
                schema: "deployments",
                table: "deployments",
                columns: new[] { "instance_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_deployments_instance_time",
                schema: "deployments",
                table: "deployments",
                columns: new[] { "instance_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_deployments_status_active",
                schema: "deployments",
                table: "deployments",
                column: "status",
                filter: "status IN ('Pending', 'Pulling', 'Starting', 'Healthcheck', 'Swapping')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deployment_logs",
                schema: "deployments");

            migrationBuilder.DropTable(
                name: "deployments",
                schema: "deployments");
        }
    }
}
