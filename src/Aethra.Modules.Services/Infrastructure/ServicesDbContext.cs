using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure.Configurations;
using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Services.Infrastructure;

public sealed class ServicesDbContext(DbContextOptions<ServicesDbContext> options)
    : AethraModuleDbContext(options)
{
    public override string SchemaName => "services";

    public DbSet<ManagedService> ManagedServices => Set<ManagedService>();
    public DbSet<ServiceBinding> ServiceBindings => Set<ServiceBinding>();
    public DbSet<ServiceBackup> ServiceBackups => Set<ServiceBackup>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new ManagedServiceConfiguration());
        modelBuilder.ApplyConfiguration(new ServiceBindingConfiguration());
        modelBuilder.ApplyConfiguration(new ServiceBackupConfiguration());
    }
}
