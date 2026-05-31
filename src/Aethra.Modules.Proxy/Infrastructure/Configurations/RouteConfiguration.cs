using Aethra.Modules.Proxy.Domain;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Proxy.Infrastructure.Configurations;

internal sealed class RouteConfiguration : IEntityTypeConfiguration<Route>
{
    public void Configure(EntityTypeBuilder<Route> builder)
    {
        builder.ToTable("routes");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<RouteId, string>(
                id => id.ToString(),
                s => ParseRouteId(s)))
            .HasMaxLength(64);

        builder.Property(r => r.Hostname)
            .HasColumnName("hostname")
            .HasConversion(new ValueConverter<Hostname, string>(
                h => h.Value,
                s => Hostname.Create(s).Value))
            .HasMaxLength(253)
            .IsRequired();

        builder.HasIndex(r => r.Hostname).IsUnique().HasDatabaseName("ux_routes_hostname");

        builder.Property(r => r.BackendUrl).HasColumnName("backend_url").HasMaxLength(512).IsRequired();
        builder.Property(r => r.TlsEnabled).HasColumnName("tls_enabled").IsRequired();
        builder.Property(r => r.CertificateId)
            .HasColumnName("certificate_id")
            .HasConversion(new ValueConverter<CertificateId?, string?>(
                id => id == null ? null : id.Value.ToString(),
                s => string.IsNullOrEmpty(s) ? null : ParseCertificateId(s)))
            .HasMaxLength(64);
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Ignore(r => r.DomainEvents);
    }

    private static RouteId ParseRouteId(string s)
        => AethraId.TryParse(s, out var parsed) ? new RouteId(parsed.Value) : default;

    private static CertificateId ParseCertificateId(string s)
        => AethraId.TryParse(s, out var parsed) ? new CertificateId(parsed.Value) : default;
}
