using Aethra.Modules.Cloudflare.Domain;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Cloudflare.Infrastructure.Configurations;

internal sealed class DnsRecordConfiguration : IEntityTypeConfiguration<DnsRecord>
{
    public void Configure(EntityTypeBuilder<DnsRecord> builder)
    {
        builder.ToTable("dns_records");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<DnsRecordId, string>(
                id => id.ToString(),
                s => ParseRecordId(s)))
            .HasMaxLength(64);

        builder.Property(r => r.ZoneId)
            .HasColumnName("zone_id")
            .HasConversion(new ValueConverter<CloudflareZoneId, string>(
                id => id.ToString(),
                s => ParseZoneId(s)))
            .HasMaxLength(64)
            .IsRequired();
        builder.HasIndex(r => r.ZoneId).HasDatabaseName("ix_dns_records_zone_id");

        builder.Property(r => r.ExternalRecordId)
            .HasColumnName("external_record_id")
            .HasMaxLength(64);
        builder.HasIndex(r => r.ExternalRecordId).HasDatabaseName("ix_dns_records_external_record_id");

        builder.Property(r => r.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(r => r.Name)
            .HasColumnName("name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(r => r.Content)
            .HasColumnName("content")
            .HasMaxLength(2048)
            .IsRequired();

        builder.Property(r => r.Ttl).HasColumnName("ttl").IsRequired();
        builder.Property(r => r.Proxied).HasColumnName("proxied").IsRequired();
        builder.Property(r => r.Comment).HasColumnName("comment").HasMaxLength(1024);

        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(r => r.SyncedAt).HasColumnName("synced_at");
        builder.Property(r => r.LastError).HasColumnName("last_error").HasColumnType("text");

        builder.Ignore(r => r.DomainEvents);
    }

    private static DnsRecordId ParseRecordId(string s)
        => AethraId.TryParse(s, out var parsed) ? new DnsRecordId(parsed.Value) : default;

    private static CloudflareZoneId ParseZoneId(string s)
        => AethraId.TryParse(s, out var parsed) ? new CloudflareZoneId(parsed.Value) : default;
}
