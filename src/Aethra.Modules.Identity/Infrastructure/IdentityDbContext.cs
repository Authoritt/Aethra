using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure.Configurations;
using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Identity.Infrastructure;

/// <summary>
/// DbContext del módulo Identity. Schema PostgreSQL: <c>identity</c>. Hereda
/// <c>outbox_messages</c> de la base.
///
/// F6+: persiste <see cref="ApiKey"/> con scopes para futuro consumo desde un MCP
/// server (F7). El <see cref="SingleUserStore"/> permanece en memoria por
/// simplicidad — en multi-user habría un <c>users</c> table aquí también.
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : AethraModuleDbContext(options)
{
    public override string SchemaName => "identity";

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new ApiKeyConfiguration());
    }
}
