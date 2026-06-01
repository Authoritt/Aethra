using Aethra.Modules.Deployments.Domain.Deployment;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Deployments.Infrastructure.Deployment;

/// <summary>
/// Mapeo EF Core de <see cref="DeploymentLogEntry"/>. Tabla <c>deployment_logs</c>.
/// FK cascade contra <c>deployments</c> — borrar un deployment borra sus logs.
/// </summary>
internal sealed class DeploymentLogEntryConfiguration : IEntityTypeConfiguration<DeploymentLogEntry>
{
    public void Configure(EntityTypeBuilder<DeploymentLogEntry> builder)
    {
        builder.ToTable("deployment_logs");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<DeploymentLogId, string>(
                id => id.ToString(),
                s => ParseDeploymentLogId(s)))
            .HasMaxLength(64);

        builder.Property(l => l.DeploymentId)
            .HasColumnName("deployment_id")
            .HasConversion(new ValueConverter<DeploymentId, string>(
                id => id.ToString(),
                s => ParseDeploymentId(s)))
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(l => l.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(l => l.Timestamp).HasColumnName("timestamp").IsRequired();
        builder.Property(l => l.Level)
            .HasColumnName("level")
            .HasConversion<string>()
            .HasMaxLength(8)
            .IsRequired();
        builder.Property(l => l.Stage)
            .HasColumnName("stage")
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(l => l.Text)
            .HasColumnName("text")
            .IsRequired();

        // (deployment_id, sequence) único: orden estable de las líneas dentro de un deployment.
        builder.HasIndex(l => new { l.DeploymentId, l.Sequence })
            .HasDatabaseName("ix_deployment_logs_deployment_seq")
            .IsUnique();
    }

    private static DeploymentLogId ParseDeploymentLogId(string s)
        => AethraId.TryParse(s, out var parsed) ? new DeploymentLogId(parsed.Value) : default;

    private static DeploymentId ParseDeploymentId(string s)
        => AethraId.TryParse(s, out var parsed) ? new DeploymentId(parsed.Value) : default;
}
