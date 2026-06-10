using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aethra.Modules.Proxy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RouteOperationalOwnerBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE proxy.routes AS r
                SET
                    operational_owner_type = 'app_environment',
                    operational_owner_id = i.id,
                    origin = 'backfill_hostname'
                FROM projects.instances AS i
                WHERE r.operational_owner_id IS NULL
                  AND (
                      lower(r.hostname) = lower(i.custom_domain)
                      OR lower(r.hostname) = lower(i.auto_hostname)
                  );
                """);

            migrationBuilder.Sql("""
                UPDATE proxy.routes AS r
                SET
                    operational_owner_type = 'app_environment',
                    operational_owner_id = i.id,
                    origin = 'backfill_backend'
                FROM projects.instances AS i
                WHERE r.operational_owner_id IS NULL
                  AND (
                      r.backend_url LIKE ('http://' || i.container_name || ':%')
                      OR r.backend_url LIKE ('https://' || i.container_name || ':%')
                      OR r.backend_url LIKE ('http://' || i.slug || '-%')
                      OR r.backend_url LIKE ('https://' || i.slug || '-%')
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE proxy.routes
                SET
                    operational_owner_type = NULL,
                    operational_owner_id = NULL,
                    origin = NULL
                WHERE origin IN ('backfill_hostname', 'backfill_backend');
                """);
        }
    }
}
