using Aethra.Modules.Settings.Domain;
using Aethra.Modules.Settings.Infrastructure.Configurations;
using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Settings.Infrastructure;

/// <summary>
/// DbContext del módulo Settings. Schema PostgreSQL: <c>settings</c>. Hereda
/// <c>outbox_messages</c> de la base. Persiste credenciales externas cifradas, base
/// domain activa única, y catálogo de ambientes válidos.
/// </summary>
public sealed class SettingsDbContext(DbContextOptions<SettingsDbContext> options)
    : AethraModuleDbContext(options)
{
    public override string SchemaName => "settings";

    public DbSet<IntegrationCredential> IntegrationCredentials => Set<IntegrationCredential>();
    public DbSet<BaseDomain> BaseDomains => Set<BaseDomain>();
    public DbSet<EnvironmentDefinition> EnvironmentDefinitions => Set<EnvironmentDefinition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new IntegrationCredentialConfiguration());
        modelBuilder.ApplyConfiguration(new BaseDomainConfiguration());
        modelBuilder.ApplyConfiguration(new EnvironmentDefinitionConfiguration());
    }
}
