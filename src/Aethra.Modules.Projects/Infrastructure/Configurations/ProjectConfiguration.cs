using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Infrastructure.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aethra.Modules.Projects.Infrastructure.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasConversion(ValueConverters.ProjectIdConverter)
            .HasMaxLength(64);

        builder.Property(p => p.Slug)
            .HasColumnName("slug")
            .HasConversion(ValueConverters.SlugConverter)
            .HasMaxLength(64)
            .IsRequired();
        builder.HasIndex(p => p.Slug).IsUnique().HasDatabaseName("ux_projects_slug");

        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(p => p.Description).HasColumnName("description").HasMaxLength(2000);
        builder.Property(p => p.Color).HasColumnName("color").HasMaxLength(16);
        builder.Property(p => p.Icon).HasColumnName("icon").HasMaxLength(64);
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasMany(p => p.Environments)
            .WithOne()
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Project.Environments))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(p => p.DomainEvents);
    }
}
