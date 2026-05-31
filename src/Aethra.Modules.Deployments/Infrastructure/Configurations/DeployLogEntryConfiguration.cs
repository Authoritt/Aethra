using Aethra.Modules.Deployments.Domain;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Deployments.Infrastructure.Configurations;

internal sealed class DeployLogEntryConfiguration : IEntityTypeConfiguration<DeployLogEntry>
{
    public void Configure(EntityTypeBuilder<DeployLogEntry> builder)
    {
        builder.ToTable("deploy_logs");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<DeployLogId, string>(
                id => id.ToString(),
                s => ParseDeployLogId(s)))
            .HasMaxLength(64);

        builder.Property(l => l.JobId)
            .HasColumnName("job_id")
            .HasConversion(new ValueConverter<DeployJobId, string>(
                id => id.ToString(),
                s => ParseDeployJobId(s)))
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(l => l.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(l => l.Timestamp).HasColumnName("timestamp").IsRequired();
        builder.Property(l => l.Level).HasColumnName("level").HasConversion<string>().HasMaxLength(8).IsRequired();
        builder.Property(l => l.Stage).HasColumnName("stage").HasMaxLength(32).IsRequired();
        builder.Property(l => l.Text).HasColumnName("text").IsRequired();

        // Index para query "logs de un job en orden":
        builder.HasIndex(l => new { l.JobId, l.Sequence })
            .HasDatabaseName("ix_deploy_logs_job_seq")
            .IsUnique();
    }

    private static DeployLogId ParseDeployLogId(string s)
        => AethraId.TryParse(s, out var p) ? new DeployLogId(p.Value) : default;

    private static DeployJobId ParseDeployJobId(string s)
        => AethraId.TryParse(s, out var p) ? new DeployJobId(p.Value) : default;
}
