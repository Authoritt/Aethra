using System.Text.Json;
using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Domain.Templates;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Projects.Infrastructure.Configurations;

/// <summary>
/// Mapeo EF Core del aggregate <see cref="Template"/>. Schema: <c>projects</c>.
///
/// <see cref="Template.Source"/> y <see cref="Template.Build"/> se persisten como columnas
/// owned en la misma tabla <c>templates</c> (no como tablas hijas) — son siempre cargados
/// con el aggregate y no se consultan por separado. Las colecciones internas
/// (<c>WatchPaths</c> y <c>BuildArgs</c>) se serializan a <c>jsonb</c>.
/// </summary>
internal sealed class TemplateConfiguration : IEntityTypeConfiguration<Template>
{
    // Cacheado (CA1869): reutilizado en cada ida/vuelta de los converters jsonb. Defaults son OK
    // porque los contenidos son internos y no se exponen vía API tal cual.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    public void Configure(EntityTypeBuilder<Template> builder)
    {
        builder.ToTable("templates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<TemplateId, string>(
                id => id.ToString(),
                s => ParseTemplateId(s)))
            .HasMaxLength(64);

        builder.Property(t => t.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(new ValueConverter<ProjectId, string>(
                id => id.ToString(),
                s => ParseProjectId(s)))
            .HasMaxLength(64)
            .IsRequired();

        // FK explícito a projects.id. Restrict: no permitir borrar un Project con Templates vivos
        // (rompería deploys y referencias en bindings de servicios).
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(t => t.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.ProjectId).HasDatabaseName("ix_templates_project_id");

        builder.Property(t => t.Slug)
            .HasColumnName("slug")
            .HasConversion(new ValueConverter<Slug, string>(
                s => s.Value,
                v => Slug.Create(v).Value))
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(t => new { t.ProjectId, t.Slug })
            .IsUnique()
            .HasDatabaseName("ux_templates_project_slug");

        builder.Property(t => t.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(t => t.Description).HasColumnName("description").HasMaxLength(2000);

        builder.Property(t => t.WebhookSecret)
            .HasColumnName("webhook_secret")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.OwnsOne(t => t.Source, src =>
        {
            src.Property(s => s.GitRepoUrl)
                .HasColumnName("source_git_repo_url")
                .HasConversion(new ValueConverter<GitRepoUrl, string>(
                    g => g.Value,
                    v => GitRepoUrl.Create(v).Value))
                .HasMaxLength(500)
                .IsRequired();

            src.Property(s => s.Branch).HasColumnName("source_branch").HasMaxLength(255).IsRequired();
            src.Property(s => s.BaseDirectory).HasColumnName("source_base_directory").HasMaxLength(255).IsRequired();
            src.Property(s => s.AccessTokenCredentialName)
                .HasColumnName("source_access_token_credential_name")
                .HasMaxLength(128);

            src.Property(s => s.WatchPaths)
                .HasColumnName("source_watch_paths")
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => DeserializeStringList(v))
                .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<string>>(
                    (a, b) => StringListsEqual(a, b),
                    v => v == null ? 0 : v.Aggregate(0, (h, item) => HashCode.Combine(h, item)),
                    v => CloneStringList(v)));
        });

        builder.OwnsOne(t => t.Build, b =>
        {
            b.Property(x => x.BuildType)
                .HasColumnName("build_type")
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            b.Property(x => x.DockerfilePath)
                .HasColumnName("build_dockerfile_path")
                .HasMaxLength(512)
                .IsRequired();

            b.Property(x => x.ComposeFilePath)
                .HasColumnName("build_compose_file_path")
                .HasMaxLength(512);

            b.Property(x => x.BuildArgs)
                .HasColumnName("build_args")
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => DeserializeBuildArgs(v))
                .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<KeyValuePair<string, string>>>(
                    (a, b) => BuildArgsEqual(a, b),
                    v => v == null
                        ? 0
                        : v.Aggregate(0, (h, kv) => HashCode.Combine(h, kv.Key, kv.Value)),
                    v => CloneBuildArgs(v)));
        });

        builder.Ignore(t => t.DomainEvents);
    }

    private static TemplateId ParseTemplateId(string s)
        => AethraId.TryParse(s, out var parsed) ? new TemplateId(parsed.Value) : default;

    private static ProjectId ParseProjectId(string s)
        => AethraId.TryParse(s, out var parsed) ? new ProjectId(parsed.Value) : default;

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

    private static List<KeyValuePair<string, string>> DeserializeBuildArgs(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new List<KeyValuePair<string, string>>();
        }
        try
        {
            return JsonSerializer.Deserialize<List<KeyValuePair<string, string>>>(raw, JsonOptions)
                ?? new List<KeyValuePair<string, string>>();
        }
        catch (JsonException)
        {
            return new List<KeyValuePair<string, string>>();
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

    private static bool BuildArgsEqual(
        IReadOnlyList<KeyValuePair<string, string>>? a,
        IReadOnlyList<KeyValuePair<string, string>>? b)
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
            if (!string.Equals(a[i].Key, b[i].Key, StringComparison.Ordinal)
                || !string.Equals(a[i].Value, b[i].Value, StringComparison.Ordinal))
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

    private static List<KeyValuePair<string, string>> CloneBuildArgs(
        IReadOnlyList<KeyValuePair<string, string>>? source)
    {
        if (source is null)
        {
            return new List<KeyValuePair<string, string>>();
        }
        var copy = new List<KeyValuePair<string, string>>(source.Count);
        copy.AddRange(source);
        return copy;
    }
}
