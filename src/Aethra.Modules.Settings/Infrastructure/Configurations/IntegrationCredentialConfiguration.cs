using System.Text.Json;
using Aethra.Modules.Settings.Domain;
using Aethra.Modules.Settings.Infrastructure.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aethra.Modules.Settings.Infrastructure.Configurations;

internal sealed class IntegrationCredentialConfiguration : IEntityTypeConfiguration<IntegrationCredential>
{
    // Cacheado (CA1869): se reutiliza en cada ida/vuelta del converter. Las opciones por
    // defecto bastan porque Metadata es un Dictionary<string,string> sin polymorphism.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    public void Configure(EntityTypeBuilder<IntegrationCredential> builder)
    {
        builder.ToTable("integration_credentials");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasConversion(ValueConverters.IntegrationCredentialIdConverter)
            .HasMaxLength(64);

        builder.Property(c => c.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(c => c.Name)
            .IsUnique()
            .HasDatabaseName("ux_integration_credentials_name");

        builder.Property(c => c.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(c => c.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(c => c.Description)
            .HasColumnName("description")
            .HasMaxLength(500);

        builder.Property(c => c.ValueCipher)
            .HasColumnName("value_cipher")
            .HasColumnType("bytea")
            .IsRequired();

        // Metadata → jsonb opcional. Decisión: JSON column en lugar de tabla separada
        // (ej. integration_credential_metadata). Razones: (a) cardinalidad baja por
        // credencial (típicamente < 5 entradas, ej. account_id, region); (b) siempre
        // se lee junto con el aggregate, nunca por separado; (c) evita join extra en
        // el camino caliente del resolver. Patrón consistente con Monitor.Headers en
        // el módulo Monitoring.
        builder.Property<IReadOnlyDictionary<string, string>?>(nameof(IntegrationCredential.Metadata))
            .HasColumnName("metadata")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonOptions),
                v => DeserializeMetadata(v))
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyDictionary<string, string>?>(
                (a, b) => MetadataEqual(a, b),
                v => v == null
                    ? 0
                    : v.Aggregate(0, (hash, kv) => HashCode.Combine(hash, kv.Key, kv.Value)),
                v => v == null
                    ? null
                    : new Dictionary<string, string>(v, StringComparer.Ordinal)));

        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.RotatedAt).HasColumnName("rotated_at");
        builder.Property(c => c.LastUsedAt).HasColumnName("last_used_at");

        builder.Ignore(c => c.DomainEvents);
    }

    private static Dictionary<string, string>? DeserializeMetadata(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(raw, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool MetadataEqual(IReadOnlyDictionary<string, string>? a, IReadOnlyDictionary<string, string>? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }
        if (a is null || b is null)
        {
            return false;
        }
        if (a.Count != b.Count)
        {
            return false;
        }
        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var v) || !string.Equals(v, kv.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }
}
