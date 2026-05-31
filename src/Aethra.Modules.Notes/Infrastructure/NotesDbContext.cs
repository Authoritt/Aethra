using Aethra.Modules.Notes.Domain;
using Aethra.Modules.Notes.Infrastructure.Configurations;
using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Notes.Infrastructure;

/// <summary>
/// DbContext del módulo Notes. Schema PostgreSQL: <c>notes</c>. Hereda <c>outbox_messages</c>
/// de la base.
/// </summary>
public sealed class NotesDbContext(DbContextOptions<NotesDbContext> options)
    : AethraModuleDbContext(options)
{
    public override string SchemaName => "notes";

    public DbSet<Note> Notes => Set<Note>();
    public DbSet<PinnedFact> PinnedFacts => Set<PinnedFact>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new NoteConfiguration());
        modelBuilder.ApplyConfiguration(new PinnedFactConfiguration());
    }
}
