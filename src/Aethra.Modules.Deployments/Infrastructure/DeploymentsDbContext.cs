using Aethra.Modules.Deployments.Domain;
using Aethra.Modules.Deployments.Infrastructure.Configurations;
using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Deployments.Infrastructure;

public sealed class DeploymentsDbContext(DbContextOptions<DeploymentsDbContext> options)
    : AethraModuleDbContext(options)
{
    public override string SchemaName => "deployments";

    public DbSet<DeployJob> DeployJobs => Set<DeployJob>();
    public DbSet<DeployLogEntry> DeployLogs => Set<DeployLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new DeployJobConfiguration());
        modelBuilder.ApplyConfiguration(new DeployLogEntryConfiguration());
    }
}
