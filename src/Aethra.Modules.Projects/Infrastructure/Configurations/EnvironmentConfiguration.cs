using Aethra.Modules.Projects.Infrastructure.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aethra.Modules.Projects.Infrastructure.Configurations;

internal sealed class EnvironmentConfiguration : IEntityTypeConfiguration<Domain.Environment>
{
    public void Configure(EntityTypeBuilder<Domain.Environment> builder)
    {
        builder.ToTable("environments");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasConversion(ValueConverters.EnvironmentIdConverter)
            .HasMaxLength(64);

        builder.Property(e => e.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(ValueConverters.ProjectIdConverter)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(e => new { e.ProjectId, e.Name })
            .IsUnique()
            .HasDatabaseName("ux_environments_project_name");

        builder.HasMany(e => e.Applications)
            .WithOne()
            .HasForeignKey(a => a.EnvironmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Domain.Environment.Applications))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
