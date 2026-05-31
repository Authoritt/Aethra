using Aethra.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Shared.Infrastructure.Persistence;

/// <summary>
/// Base para todos los DbContext de módulo. Cada subclase declara su <see cref="SchemaName"/>
/// y registra sus propias entidades, pero comparte la tabla <c>outbox_messages</c> dentro
/// de su schema.
///
/// Convención: un schema por módulo (projects, deployments, vms, ...).
/// Esto fuerza la frontera incluso a nivel BD: cross-schema reads requieren queries explícitos.
/// </summary>
public abstract class AethraModuleDbContext : DbContext
{
    protected AethraModuleDbContext(DbContextOptions options) : base(options) { }

    /// <summary>
    /// Nombre del schema PostgreSQL (lowercase, snake_case). Ej: "projects", "deployments".
    /// </summary>
    public abstract string SchemaName { get; }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
