using Aethra.Modules.Deployments.Domain.Deployment;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Deployments.Infrastructure.Deployment;

/// <summary>
/// Mapeo EF Core del agregado <see cref="Deployment"/>. Tabla <c>deployments</c>.
///
/// Decisiones:
/// <list type="bullet">
///   <item>El <c>DeploymentId</c> se persiste como string (formato <c>dep_xxx</c>) — mismo patrón
///         que <c>BuildId</c>. Esto facilita inspección directa en BD sin acoplar a Guid v7.</item>
///   <item><c>BuildId</c> e <c>InstanceId</c> son strings sin FK física: el primero pertenece al
///         mismo schema pero al aggregate root <c>Build</c> (no queremos un FK que dispare cascades
///         entre aggregates), el segundo es cross-module (Projects). Los índices por estas dos
///         columnas son los que dan performance al lookup "deploys activos por instance" y
///         "todos los deploys de un build".</item>
///   <item>Índice filtrado por status activo para el (futuro F9.4) recovery host que reanuda
///         deployments interrumpidos por reinicio del central.</item>
///   <item><c>DomainEvents</c> se ignora — los publica el SaveChangesInterceptor del módulo (no
///         persiste, no se traduce a outbox aquí; el orquestador lo hace explícitamente).</item>
/// </list>
/// </summary>
internal sealed class DeploymentConfiguration : IEntityTypeConfiguration<Domain.Deployment.Deployment>
{
    public void Configure(EntityTypeBuilder<Domain.Deployment.Deployment> builder)
    {
        builder.ToTable("deployments");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<DeploymentId, string>(
                id => id.ToString(),
                s => ParseDeploymentId(s)))
            .HasMaxLength(64);

        builder.Property(d => d.BuildId)
            .HasColumnName("build_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(d => d.InstanceId)
            .HasColumnName("instance_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(d => d.Trigger)
            .HasColumnName("trigger")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(d => d.TriggeredBy)
            .HasColumnName("triggered_by")
            .HasMaxLength(255);

        builder.Property(d => d.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(d => d.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(d => d.StartedAt).HasColumnName("started_at");
        builder.Property(d => d.FinishedAt).HasColumnName("finished_at");

        builder.Property(d => d.OldContainerId)
            .HasColumnName("old_container_id")
            .HasMaxLength(255);

        builder.Property(d => d.NewContainerId)
            .HasColumnName("new_container_id")
            .HasMaxLength(255);

        builder.Property(d => d.OldImageRef)
            .HasColumnName("old_image_ref")
            .HasMaxLength(512);

        builder.Property(d => d.NewImageRef)
            .HasColumnName("new_image_ref")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(d => d.ErrorCode)
            .HasColumnName("error_code")
            .HasMaxLength(64);

        builder.Property(d => d.ErrorMessage)
            .HasColumnName("error_message")
            .HasMaxLength(4000);

        builder.Property(d => d.FailedAtStage)
            .HasColumnName("failed_at_stage")
            .HasConversion<string?>()
            .HasMaxLength(16);

        // (instance_id, status) → lookup del deployment activo por instance (state machine
        // del proxy/instance). No único: una Instance puede tener histórico amplio.
        builder.HasIndex(d => new { d.InstanceId, d.Status })
            .HasDatabaseName("ix_deployments_instance_status");

        // build_id → todos los deployments derivados de un mismo build (1 build → N deploys).
        builder.HasIndex(d => d.BuildId)
            .HasDatabaseName("ix_deployments_build");

        // (instance_id, created_at desc) → listado UI ordenado por más recientes.
        builder.HasIndex(d => new { d.InstanceId, d.CreatedAt })
            .HasDatabaseName("ix_deployments_instance_time")
            .IsDescending(false, true);

        // Index filtrado para recovery: deployments interrumpidos tras un reinicio del central.
        builder.HasIndex(d => d.Status)
            .HasDatabaseName("ix_deployments_status_active")
            .HasFilter("status IN ('Pending', 'Pulling', 'Starting', 'Healthcheck', 'Swapping')");

        builder.HasMany(d => d.Logs)
            .WithOne()
            .HasForeignKey(l => l.DeploymentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Domain.Deployment.Deployment.Logs))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(d => d.DomainEvents);
    }

    private static DeploymentId ParseDeploymentId(string s)
        => AethraId.TryParse(s, out var parsed) ? new DeploymentId(parsed.Value) : default;
}
