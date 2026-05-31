using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.Infrastructure;

/// <summary>
/// DbContext del módulo Projects. Schema PostgreSQL: <c>projects</c>.
/// Hereda outbox_messages de la base.
///
/// Estado actual (F9.0 cleanup): vacío de DbSets. La sub-fase persistence reintroducirá los
/// DbSets de Project + EnvironmentVariable + los aggregates de A1 (Template, Client, Instance)
/// con sus respectivas configuraciones, y se regenerarán las migraciones desde cero.
/// </summary>
public sealed class ProjectsDbContext(DbContextOptions<ProjectsDbContext> options)
    : AethraModuleDbContext(options)
{
    public override string SchemaName => "projects";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // F9.0 persistence sub-fase añadirá ApplyConfiguration() para Project, EnvironmentVariable,
        // Template, Client, Instance y sus owned entities (TemplateSource, TemplateBuild, etc.).
    }
}
