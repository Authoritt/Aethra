using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Deployments.Infrastructure;

/// <summary>
/// DbContext del módulo Deployments. Schema PostgreSQL: <c>deployments</c>.
/// Hereda outbox_messages de la base.
///
/// Estado F9.0 cleanup: vacío de DbSets. F9.3/F9.4 reintroducirán las entidades del nuevo
/// modelo (Build, DeployTask) con sus configurations y migraciones desde cero.
/// </summary>
public sealed class DeploymentsDbContext(DbContextOptions<DeploymentsDbContext> options)
    : AethraModuleDbContext(options)
{
    public override string SchemaName => "deployments";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // F9.3/F9.4 añadirá ApplyConfiguration() para Build, DeployTask, etc.
    }
}
