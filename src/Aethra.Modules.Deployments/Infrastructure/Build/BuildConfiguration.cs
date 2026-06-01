using Aethra.Modules.Deployments.Domain.Build;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using BuildEntity = Aethra.Modules.Deployments.Domain.Build.Build;

namespace Aethra.Modules.Deployments.Infrastructure.Build;

/// <summary>
/// Mapeo EF Core del agregado <see cref="Build"/>. Tabla <c>builds</c>.
///
/// Decisiones:
/// - El <c>BuildId</c> se persiste como string (formato prefix_xxx) — mismo patrón que
///   <c>RouteConfiguration</c>. Esto evita acoplar el schema a Guid v7 y permite filtrar
///   por prefijo en logs si se necesita.
/// - El índice por <c>Status</c> está filtrado a estados no terminales — sirve solo al
///   dispatcher que reanuda builds en cola tras un reinicio (F9.3.5 recovery).
/// - <c>DomainEvents</c> se ignora — los publica el SaveChangesInterceptor del módulo.
/// </summary>
internal sealed class BuildConfiguration : IEntityTypeConfiguration<BuildEntity>
{
    public void Configure(EntityTypeBuilder<BuildEntity> builder)
    {
        builder.ToTable("builds");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<BuildId, string>(
                id => id.ToString(),
                s => ParseBuildId(s)))
            .HasMaxLength(64);

        builder.Property(b => b.TemplateId)
            .HasColumnName("template_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(b => b.GitSha)
            .HasColumnName("git_sha")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(b => b.GitRef)
            .HasColumnName("git_ref")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(b => b.Trigger)
            .HasColumnName("trigger")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(b => b.TriggeredBy)
            .HasColumnName("triggered_by")
            .HasMaxLength(255);

        builder.Property(b => b.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(b => b.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(b => b.StartedAt).HasColumnName("started_at");
        builder.Property(b => b.FinishedAt).HasColumnName("finished_at");

        builder.Property(b => b.ImageRef)
            .HasColumnName("image_ref")
            .HasMaxLength(512);

        builder.Property(b => b.BuildDurationMs).HasColumnName("build_duration_ms");

        builder.Property(b => b.ErrorCode)
            .HasColumnName("error_code")
            .HasMaxLength(64);

        builder.Property(b => b.ErrorMessage)
            .HasColumnName("error_message")
            .HasMaxLength(4000);

        builder.Property(b => b.FailedAtStage)
            .HasColumnName("failed_at_stage")
            .HasConversion<string?>()
            .HasMaxLength(16);

        // (template_id, git_sha) → lookup rápido de builds previos del mismo commit. NO es
        // único: un mismo commit puede haber generado varios intentos (manual + webhook).
        builder.HasIndex(b => new { b.TemplateId, b.GitSha })
            .HasDatabaseName("ix_builds_template_sha");

        // (template_id, created_at desc) → listado UI ordenado por más reciente.
        builder.HasIndex(b => new { b.TemplateId, b.CreatedAt })
            .HasDatabaseName("ix_builds_template_time")
            .IsDescending(false, true);

        // Index filtrado para el dispatcher que reanuda builds tras un reinicio del central.
        // Solo cubre estados no terminales — los terminales se acumulan sin coste extra.
        builder.HasIndex(b => b.Status)
            .HasDatabaseName("ix_builds_status_active")
            .HasFilter("status IN ('Queued', 'Cloning', 'Building', 'Pushing')");

        builder.HasMany(b => b.Logs)
            .WithOne()
            .HasForeignKey(l => l.BuildId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(BuildEntity.Logs))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(b => b.DomainEvents);
    }

    private static BuildId ParseBuildId(string s)
        => AethraId.TryParse(s, out var parsed) ? new BuildId(parsed.Value) : default;
}
