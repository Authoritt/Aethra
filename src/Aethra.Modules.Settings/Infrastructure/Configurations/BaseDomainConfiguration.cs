using Aethra.Modules.Settings.Domain;
using Aethra.Modules.Settings.Infrastructure.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aethra.Modules.Settings.Infrastructure.Configurations;

internal sealed class BaseDomainConfiguration : IEntityTypeConfiguration<BaseDomain>
{
    public void Configure(EntityTypeBuilder<BaseDomain> builder)
    {
        builder.ToTable("base_domains");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasColumnName("id")
            .HasConversion(ValueConverters.BaseDomainIdConverter)
            .HasMaxLength(64);

        builder.Property(d => d.Hostname)
            .HasColumnName("hostname")
            .HasMaxLength(253)
            .IsRequired();

        builder.HasIndex(d => d.Hostname)
            .IsUnique()
            .HasDatabaseName("ux_base_domains_hostname");

        builder.Property(d => d.CloudflareZoneId)
            .HasColumnName("cloudflare_zone_id")
            .HasMaxLength(64);

        builder.Property(d => d.WildcardConfigured)
            .HasColumnName("wildcard_configured")
            .IsRequired();

        builder.Property(d => d.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        // Índice parcial sobre IsActive para acelerar GetActiveAsync (1 row max).
        builder.HasIndex(d => d.IsActive)
            .HasDatabaseName("ix_base_domains_is_active");

        builder.Property(d => d.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Ignore(d => d.DomainEvents);
    }
}
