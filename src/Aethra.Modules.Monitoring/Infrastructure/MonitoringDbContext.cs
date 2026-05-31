using Aethra.Modules.Monitoring.Domain;
using Aethra.Modules.Monitoring.Infrastructure.Configurations;
using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Monitoring.Infrastructure;

public sealed class MonitoringDbContext(DbContextOptions<MonitoringDbContext> options)
    : AethraModuleDbContext(options)
{
    public override string SchemaName => "monitoring";

    public DbSet<Monitor> Monitors => Set<Monitor>();
    public DbSet<MonitorCheck> MonitorChecks => Set<MonitorCheck>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new MonitorConfiguration());
        modelBuilder.ApplyConfiguration(new MonitorCheckConfiguration());
    }
}
