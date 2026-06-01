using System.Text.Json;
using Aethra.Modules.Projects.Domain.Clients;
using Aethra.Modules.Projects.Domain.Instances;
using Aethra.Modules.Projects.Domain.Templates;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Projects.Infrastructure.Configurations;

/// <summary>
/// Mapeo EF Core del aggregate <see cref="Instance"/>. Schema: <c>projects</c>.
///
/// Las colecciones (<c>Ports</c>, <c>Volumes</c>) se persisten como tablas hijas con FK
/// <c>instance_id</c>; el <c>Healthcheck</c> se persiste como owned columns en la misma fila
/// porque siempre es 1:1 y nunca se consulta por separado. La FK al Project queda implícita
/// (se navega via Template.ProjectId) — no se persiste columna directa.
/// </summary>
internal sealed class InstanceConfiguration : IEntityTypeConfiguration<Instance>
{
    // Cacheado (CA1869): reutilizado en el converter de Healthcheck.Test (lista shell args).
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    public void Configure(EntityTypeBuilder<Instance> builder)
    {
        builder.ToTable("instances");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<InstanceId, string>(
                id => id.ToString(),
                s => ParseInstanceId(s)))
            .HasMaxLength(64);

        builder.Property(i => i.TemplateId)
            .HasColumnName("template_id")
            .HasConversion(new ValueConverter<TemplateId, string>(
                id => id.ToString(),
                s => ParseTemplateId(s)))
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(i => i.ClientId)
            .HasColumnName("client_id")
            .HasConversion(new ValueConverter<ClientId, string>(
                id => id.ToString(),
                s => ParseClientId(s)))
            .HasMaxLength(64)
            .IsRequired();

        // FKs explícitas. Restrict en ambos casos: no permitir borrar Template ni Client
        // con Instances vivas (rompería deploys activos y dejaría contenedores huérfanos).
        builder.HasOne<Template>()
            .WithMany()
            .HasForeignKey(i => i.TemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Client>()
            .WithMany()
            .HasForeignKey(i => i.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => i.TemplateId).HasDatabaseName("ix_instances_template_id");
        builder.HasIndex(i => i.ClientId).HasDatabaseName("ix_instances_client_id");

        builder.Property(i => i.Environment).HasColumnName("environment").HasMaxLength(32).IsRequired();

        builder.Property(i => i.Slug).HasColumnName("slug").HasMaxLength(64).IsRequired();
        builder.HasIndex(i => new { i.TemplateId, i.Slug })
            .IsUnique()
            .HasDatabaseName("ux_instances_template_slug");

        builder.Property(i => i.TargetVmId).HasColumnName("target_vm_id").HasMaxLength(64).IsRequired();
        builder.Property(i => i.ContainerName).HasColumnName("container_name").HasMaxLength(255).IsRequired();

        builder.Property(i => i.AutoDeployOnNewBuild).HasColumnName("auto_deploy_on_new_build").IsRequired();
        builder.Property(i => i.CustomDomain).HasColumnName("custom_domain").HasMaxLength(253);
        builder.Property(i => i.AutoHostname).HasColumnName("auto_hostname").HasMaxLength(253);

        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.OwnsMany(i => i.Ports, p =>
        {
            p.ToTable("instance_ports");

            // Owner es Instance (Id: InstanceId con conversion a string). El shadow FK debe tipar
            // el mismo CLR type que el PK del owner para que EF infiera el converter.
            p.WithOwner().HasForeignKey("InstanceId");
            p.HasKey("InstanceId", "ContainerPort");

            p.Property<InstanceId>("InstanceId")
                .HasColumnName("instance_id")
                .HasConversion(new ValueConverter<InstanceId, string>(
                    id => id.ToString(),
                    s => ParseInstanceId(s)))
                .HasMaxLength(64);

            p.Property(x => x.ContainerPort)
                .HasColumnName("container_port")
                .HasConversion(new ValueConverter<Port, int>(
                    port => port.Value,
                    v => Port.Create(v).Value))
                .IsRequired();

            p.Property(x => x.HostPort).HasColumnName("host_port");

            p.Property(x => x.Protocol)
                .HasColumnName("protocol")
                .HasConversion<string>()
                .HasMaxLength(8)
                .IsRequired();
        });

        builder.Metadata.FindNavigation(nameof(Instance.Ports))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(i => i.Volumes, v =>
        {
            v.ToTable("instance_volumes");
            v.WithOwner().HasForeignKey("InstanceId");
            v.HasKey("InstanceId", "Name");

            v.Property<InstanceId>("InstanceId")
                .HasColumnName("instance_id")
                .HasConversion(new ValueConverter<InstanceId, string>(
                    id => id.ToString(),
                    s => ParseInstanceId(s)))
                .HasMaxLength(64);

            v.Property(x => x.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
            v.Property(x => x.ContainerPath).HasColumnName("container_path").HasMaxLength(500).IsRequired();
            v.Property(x => x.ReadOnly).HasColumnName("read_only").IsRequired();
        });

        builder.Metadata.FindNavigation(nameof(Instance.Volumes))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Healthcheck es opcional (Instance.Healthcheck es nullable). EF mantiene las columnas
        // todas-nulas si no se setea — esto es OK porque el dominio nunca lee fields individuales,
        // solo el record completo.
        builder.OwnsOne(i => i.Healthcheck, hc =>
        {
            hc.Property(x => x.Test)
                .HasColumnName("hc_test")
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => DeserializeStringList(v))
                .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<string>>(
                    (a, b) => StringListsEqual(a, b),
                    v => v == null ? 0 : v.Aggregate(0, (h, item) => HashCode.Combine(h, item)),
                    v => CloneStringList(v)));

            hc.Property(x => x.IntervalSeconds).HasColumnName("hc_interval_seconds");
            hc.Property(x => x.Retries).HasColumnName("hc_retries");
            hc.Property(x => x.TimeoutSeconds).HasColumnName("hc_timeout_seconds");
            hc.Property(x => x.StartPeriodSeconds).HasColumnName("hc_start_period_seconds");
        });

        builder.Ignore(i => i.DomainEvents);
    }

    private static InstanceId ParseInstanceId(string s)
        => AethraId.TryParse(s, out var parsed) ? new InstanceId(parsed.Value) : default;

    private static TemplateId ParseTemplateId(string s)
        => AethraId.TryParse(s, out var parsed) ? new TemplateId(parsed.Value) : default;

    private static ClientId ParseClientId(string s)
        => AethraId.TryParse(s, out var parsed) ? new ClientId(parsed.Value) : default;

    private static List<string> DeserializeStringList(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new List<string>();
        }
        try
        {
            return JsonSerializer.Deserialize<List<string>>(raw, JsonOptions) ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static bool StringListsEqual(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
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
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    private static List<string> CloneStringList(IReadOnlyList<string>? source)
    {
        if (source is null)
        {
            return new List<string>();
        }
        var copy = new List<string>(source.Count);
        copy.AddRange(source);
        return copy;
    }
}
