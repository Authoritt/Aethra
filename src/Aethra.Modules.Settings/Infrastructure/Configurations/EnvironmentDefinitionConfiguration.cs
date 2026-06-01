using Aethra.Modules.Settings.Domain;
using Aethra.Modules.Settings.Infrastructure.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aethra.Modules.Settings.Infrastructure.Configurations;

internal sealed class EnvironmentDefinitionConfiguration : IEntityTypeConfiguration<EnvironmentDefinition>
{
    public void Configure(EntityTypeBuilder<EnvironmentDefinition> builder)
    {
        builder.ToTable("environment_definitions");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasConversion(ValueConverters.EnvironmentDefinitionIdConverter)
            .HasMaxLength(64);

        builder.Property(e => e.Slug)
            .HasColumnName("slug")
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(e => e.Slug)
            .IsUnique()
            .HasDatabaseName("ux_environment_definitions_slug");

        builder.Property(e => e.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Order)
            .HasColumnName("order")
            .IsRequired();

        builder.HasIndex(e => e.Order)
            .HasDatabaseName("ix_environment_definitions_order");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}
