using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Vms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VmAcceptsPreviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F12.3 — opt-in al pool round-robin de previews. Default true para que el operador
            // pueda apagar VMs específicas si quiere reservarlas a workloads productivos.
            migrationBuilder.AddColumn<bool>(
                name: "accepts_previews",
                schema: "vms",
                table: "vms",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "accepts_previews",
                schema: "vms",
                table: "vms");
        }
    }
}
