using Aethra.Modules.Projects.Domain;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Projects.Infrastructure.Configurations;

/// <summary>
/// Mapeo EF Core del aggregate root <see cref="Project"/>. Schema: <c>projects</c>.
///
/// El <see cref="Project.Slug"/> es único globalmente — F9 mantiene el invariante porque el
/// caller de los handlers nunca debería generar duplicados, pero el unique index actúa como
/// defensa final en BD.
/// </summary>
internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<ProjectId, string>(
                id => id.ToString(),
                s => ParseProjectId(s)))
            .HasMaxLength(64);

        builder.Property(p => p.Slug)
            .HasColumnName("slug")
            .HasConversion(new ValueConverter<Slug, string>(
                s => s.Value,
                v => Slug.Create(v).Value))
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(p => p.Slug).IsUnique().HasDatabaseName("ux_projects_slug");

        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(p => p.Description).HasColumnName("description").HasMaxLength(2000);
        builder.Property(p => p.Color).HasColumnName("color").HasMaxLength(32);
        builder.Property(p => p.Icon).HasColumnName("icon").HasMaxLength(64);
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // F12.3 — Preview lifecycle controls.
        builder.Property(p => p.PreviewMaxConcurrent)
            .HasColumnName("preview_max_concurrent")
            .IsRequired()
            .HasDefaultValue(10);
        builder.Property(p => p.PreviewClientId)
            .HasColumnName("preview_client_id")
            .HasMaxLength(64);

        builder.Ignore(p => p.DomainEvents);
    }

    private static ProjectId ParseProjectId(string s)
        => AethraId.TryParse(s, out var parsed) ? new ProjectId(parsed.Value) : default;
}
