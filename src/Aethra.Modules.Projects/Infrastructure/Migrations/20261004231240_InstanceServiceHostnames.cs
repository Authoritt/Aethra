using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Projects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InstanceServiceHostnames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instance_service_hostnames",
                schema: "projects",
                columns: table => new
                {
                    service_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    instance_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hostname = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instance_service_hostnames", x => new { x.instance_id, x.service_name });
                    table.ForeignKey(
                        name: "FK_instance_service_hostnames_instances_instance_id",
                        column: x => x.instance_id,
                        principalSchema: "projects",
                        principalTable: "instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instance_service_hostnames",
                schema: "projects");
        }
    }
}
