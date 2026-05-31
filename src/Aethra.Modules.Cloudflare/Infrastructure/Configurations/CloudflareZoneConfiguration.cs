using Aethra.Modules.Cloudflare.Domain;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Cloudflare.Infrastructure.Configurations;

internal sealed class CloudflareZoneConfiguration : IEntityTypeConfiguration<CloudflareZone>
{
    public void Configure(EntityTypeBuilder<CloudflareZone> builder)
    {
        builder.ToTable("zones");
        builder.HasKey(z => z.Id);

        builder.Property(z => z.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<CloudflareZoneId, string>(
                id => id.ToString(),
                s => ParseZoneId(s)))
            .HasMaxLength(64);

        builder.Property(z => z.ZoneId)
            .HasColumnName("external_zone_id")
            .HasMaxLength(64)
            .IsRequired();
        builder.HasIndex(z => z.ZoneId).IsUnique().HasDatabaseName("ux_zones_external_zone_id");

        builder.Property(z => z.Name)
            .HasColumnName("name")
            .HasMaxLength(253)
            .IsRequired();
        builder.HasIndex(z => z.Name).HasDatabaseName("ix_zones_name");

        builder.Property(z => z.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(z => z.AccountId)
            .HasColumnName("account_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(z => z.ApiTokenCipher)
            .HasColumnName("api_token_cipher")
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(z => z.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(z => z.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(z => z.LastSyncedAt).HasColumnName("last_synced_at");

        builder.Ignore(z => z.DomainEvents);
    }

    private static CloudflareZoneId ParseZoneId(string s)
        => AethraId.TryParse(s, out var parsed) ? new CloudflareZoneId(parsed.Value) : default;
}
