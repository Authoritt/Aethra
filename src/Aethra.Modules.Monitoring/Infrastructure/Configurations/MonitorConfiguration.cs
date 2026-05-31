using System.Text.Json;
using Aethra.Modules.Monitoring.Domain;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Monitoring.Infrastructure.Configurations;

internal sealed class MonitorConfiguration : IEntityTypeConfiguration<Monitor>
{
    // Cacheado (CA1869): se reutiliza en cada ida/vuelta de los converters. JsonOptions por defecto
    // está bien — el contenido es interno, no se expone vía API.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    public void Configure(EntityTypeBuilder<Monitor> builder)
    {
        builder.ToTable("monitors");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<MonitorId, string>(
                id => id.ToString(),
                s => ParseMonitorId(s)))
            .HasMaxLength(64);

        builder.Property(m => m.Slug)
            .HasColumnName("slug")
            .HasConversion(new ValueConverter<Slug, string>(
                s => s.Value,
                v => Slug.Create(v).Value))
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(m => m.Slug).IsUnique().HasDatabaseName("ux_monitors_slug");

        builder.Property(m => m.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(m => m.Url).HasColumnName("url").HasMaxLength(2048).IsRequired();
        builder.Property(m => m.HttpMethod)
            .HasColumnName("http_method")
            .HasConversion<string>()
            .HasMaxLength(8)
            .IsRequired();

        // ExpectedStatusCodes → JSON array. EF necesita un ValueComparer para que detecte cambios
        // en colecciones owned-by-value.
        builder.Property<List<int>>("_expectedStatusCodes")
            .HasField("_expectedStatusCodes")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("expected_status_codes")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => DeserializeIntList(v))
            .Metadata.SetValueComparer(new ValueComparer<List<int>>(
                (a, b) => SequencesEqual(a, b),
                v => v == null ? 0 : v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
                v => v == null ? new List<int>() : new List<int>(v)));

        builder.Ignore(m => m.ExpectedStatusCodes);

        builder.Property(m => m.IntervalSec).HasColumnName("interval_sec").IsRequired();
        builder.Property(m => m.TimeoutMs).HasColumnName("timeout_ms").IsRequired();

        // Headers → JSON object opcional. Mismo patrón que ExpectedStatusCodes.
        builder.Property<Dictionary<string, string>?>("_headers")
            .HasField("_headers")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("headers")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonOptions),
                v => DeserializeStringDict(v))
            .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, string>?>(
                (a, b) => HeadersEqual(a, b),
                v => v == null
                    ? 0
                    : v.Aggregate(0, (hash, kv) => HashCode.Combine(hash, kv.Key, kv.Value)),
                v => v == null ? null : new Dictionary<string, string>(v, StringComparer.Ordinal)));
        builder.Ignore(m => m.Headers);

        builder.Property(m => m.BodyTemplate).HasColumnName("body_template").HasColumnType("text");
        builder.Property(m => m.InstanceId).HasColumnName("instance_id").HasMaxLength(64);
        builder.Property(m => m.ProjectId).HasColumnName("project_id").HasMaxLength(64);
        builder.Property(m => m.IsEnabled).HasColumnName("is_enabled").IsRequired();

        builder.Property(m => m.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(m => m.LastCheckedAt).HasColumnName("last_checked_at");
        builder.Property(m => m.LastStatus)
            .HasColumnName("last_status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(m => m.ConsecutiveFailures).HasColumnName("consecutive_failures").IsRequired();

        // Índice del worker: filtra por enabled y ordena por LastCheckedAt asc nulls first
        // para tomar primero los que nunca se probaron o los más antiguos.
        builder.HasIndex(m => new { m.IsEnabled, m.LastCheckedAt })
            .HasDatabaseName("ix_monitors_enabled_last_checked");
        builder.HasIndex(m => m.InstanceId).HasDatabaseName("ix_monitors_instance");
        builder.HasIndex(m => m.ProjectId).HasDatabaseName("ix_monitors_project");

        builder.Ignore(m => m.DomainEvents);
    }

    private static MonitorId ParseMonitorId(string s)
        => AethraId.TryParse(s, out var parsed) ? new MonitorId(parsed.Value) : default;

    private static List<int> DeserializeIntList(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [200];
        }
        try
        {
            return JsonSerializer.Deserialize<List<int>>(raw, JsonOptions) ?? [200];
        }
        catch (JsonException)
        {
            return [200];
        }
    }

    private static Dictionary<string, string>? DeserializeStringDict(string? raw)
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

    private static bool SequencesEqual(List<int>? a, List<int>? b)
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
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }
        return true;
    }

    private static bool HeadersEqual(Dictionary<string, string>? a, Dictionary<string, string>? b)
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
