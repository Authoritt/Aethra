using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure.Configurations;
using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Identity.Infrastructure;

/// <summary>
/// DbContext del módulo Identity. Schema PostgreSQL: <c>identity</c>. Hereda
/// <c>outbox_messages</c> de la base.
///
/// F6: <see cref="ApiKey"/> con scopes para consumo desde el MCP server.
/// F11.1: <see cref="User"/> + <see cref="Role"/> + <see cref="UserRole"/> habilitan
/// multi-user con RBAC. <see cref="SingleUserStore"/> queda como fallback bootstrap
/// cuando la tabla <c>users</c> está vacía.
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : AethraModuleDbContext(options)
{
    public override string SchemaName => "identity";

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new ApiKeyConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new RoleConfiguration());
        modelBuilder.ApplyConfiguration(new UserRoleConfiguration());
    }
}
