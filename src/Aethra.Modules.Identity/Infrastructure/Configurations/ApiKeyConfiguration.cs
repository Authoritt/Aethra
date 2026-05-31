using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aethra.Modules.Identity.Infrastructure.Configurations;

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");
        builder.HasKey(k => k.Id);

        builder.Property(k => k.Id)
            .HasColumnName("id")
            .HasConversion(ValueConverters.ApiKeyIdConverter)
            .HasMaxLength(64);

        builder.Property(k => k.Name).HasColumnName("name").HasMaxLength(100).IsRequired();

        builder.Property(k => k.KeyPrefix)
            .HasColumnName("key_prefix")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(k => k.KeyHash)
            .HasColumnName("key_hash")
            .HasColumnType("bytea")
            .IsRequired();

        // Scopes como text[] (Postgres array nativo). Mismo patrón que
        // ApplicationSource.WatchPaths / Healthcheck.Cmd en Projects.
        builder.Property(k => k.Scopes)
            .HasColumnName("scopes")
            .HasColumnType("text[]")
            .HasConversion(
                v => v.ToArray(),
                v => (IReadOnlySet<string>)new HashSet<string>(v ?? Array.Empty<string>(), StringComparer.Ordinal))
            .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlySet<string>>(
                (a, b) => a == null && b == null || a != null && b != null && a.SetEquals(b),
                v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x.GetHashCode(StringComparison.Ordinal))),
                v => (IReadOnlySet<string>)new HashSet<string>(v, StringComparer.Ordinal)));

        builder.Property(k => k.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(k => k.LastUsedAt).HasColumnName("last_used_at");
        builder.Property(k => k.RevokedAt).HasColumnName("revoked_at");
        builder.Property(k => k.ExpiresAt).HasColumnName("expires_at");

        // Índice único en key_hash — habilita lookup O(log n) por hash desde
        // AethraApiKeyAuthHandler. Es único porque dos secrets random distintos
        // no pueden colisionar en su hash determinístico.
        builder.HasIndex(k => k.KeyHash)
            .IsUnique()
            .HasDatabaseName("ux_api_keys_key_hash");

        // Índice secundario para listar y buscar por estado.
        builder.HasIndex(k => k.RevokedAt)
            .HasDatabaseName("ix_api_keys_revoked_at");

        builder.Ignore(k => k.DomainEvents);
    }
}
