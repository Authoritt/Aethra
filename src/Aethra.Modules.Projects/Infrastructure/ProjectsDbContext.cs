using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Domain.Clients;
using Aethra.Modules.Projects.Domain.EnvVars;
using Aethra.Modules.Projects.Domain.Instances;
using Aethra.Modules.Projects.Domain.Secrets;
using Aethra.Modules.Projects.Domain.Templates;
using Aethra.Modules.Projects.Infrastructure.Configurations;
using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.Infrastructure;

/// <summary>
/// DbContext del módulo Projects. Schema PostgreSQL: <c>projects</c>.
/// Hereda <c>outbox_messages</c> de la base.
///
/// F9.0 persistence sub-fase: cablea los aggregates Project + Template + Client + Instance +
/// EnvironmentVariable. Los IDs (Template/Client/Instance) son value-object converters declarados
/// inline en cada <c>IEntityTypeConfiguration</c> para mantener proximidad con el mapeo.
/// </summary>
public sealed class ProjectsDbContext(DbContextOptions<ProjectsDbContext> options)
    : AethraModuleDbContext(options)
{
    public override string SchemaName => "projects";

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Instance> Instances => Set<Instance>();
    public DbSet<EnvironmentVariable> EnvironmentVariables => Set<EnvironmentVariable>();
    public DbSet<Secret> Secrets => Set<Secret>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new ProjectConfiguration());
        modelBuilder.ApplyConfiguration(new TemplateConfiguration());
        modelBuilder.ApplyConfiguration(new ClientConfiguration());
        modelBuilder.ApplyConfiguration(new InstanceConfiguration());
        modelBuilder.ApplyConfiguration(new EnvironmentVariableConfiguration());
        modelBuilder.ApplyConfiguration(new SecretConfiguration());
    }
}
