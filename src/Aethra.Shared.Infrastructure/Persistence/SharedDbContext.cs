using Microsoft.EntityFrameworkCore;

namespace Aethra.Shared.Infrastructure.Persistence;

/// <summary>
/// DbContext de utilidades transversales. Solo aloja tablas que NO pertenecen
/// a un módulo de dominio: idempotency_keys, audit_log (fase 2), feature_flags (fase 2).
///
/// Schema: "shared".
/// </summary>
public sealed class SharedDbContext(DbContextOptions<SharedDbContext> options) : DbContext(options)
{
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("shared");
        modelBuilder.ApplyConfiguration(new IdempotencyKeyConfiguration());
    }
}
