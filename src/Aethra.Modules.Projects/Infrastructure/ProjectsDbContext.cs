using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Domain.EnvVars;
using Aethra.Modules.Projects.Infrastructure.Configurations;
using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.Infrastructure;

/// <summary>
/// DbContext del módulo Projects. Schema PostgreSQL: <c>projects</c>.
/// Hereda outbox_messages de la base.
/// </summary>
public sealed class ProjectsDbContext(DbContextOptions<ProjectsDbContext> options)
    : AethraModuleDbContext(options)
{
    public override string SchemaName => "projects";

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Domain.Environment> Environments => Set<Domain.Environment>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<EnvironmentVariable> EnvironmentVariables => Set<EnvironmentVariable>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new ProjectConfiguration());
        modelBuilder.ApplyConfiguration(new EnvironmentConfiguration());
        modelBuilder.ApplyConfiguration(new ApplicationConfiguration());
        modelBuilder.ApplyConfiguration(new EnvironmentVariableConfiguration());
    }
}
