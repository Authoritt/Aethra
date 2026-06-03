using Aethra.Modules.Services.Domain;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Services.Infrastructure.Configurations;

internal sealed class ScheduledJobConfiguration : IEntityTypeConfiguration<ScheduledJob>
{
    public void Configure(EntityTypeBuilder<ScheduledJob> builder)
    {
        builder.ToTable("scheduled_jobs");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<ScheduledJobId, string>(
                id => id.ToString(),
                s => ParseScheduledJobId(s)))
            .HasMaxLength(64);

        builder.Property(j => j.ServiceId)
            .HasColumnName("service_id")
            .HasConversion(new ValueConverter<ManagedServiceId, string>(
                id => id.ToString(),
                s => ParseManagedServiceId(s)))
            .HasMaxLength(64)
            .IsRequired();

        builder.HasOne<ManagedService>()
            .WithMany()
            .HasForeignKey(j => j.ServiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(j => j.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        builder.Property(j => j.Description).HasColumnName("description").HasMaxLength(1000);
        builder.Property(j => j.Command).HasColumnName("command").HasMaxLength(2000).IsRequired();
        builder.Property(j => j.CronExpression).HasColumnName("cron_expression").HasMaxLength(64).IsRequired();
        builder.Property(j => j.TimeZone).HasColumnName("time_zone").HasMaxLength(64).IsRequired();
        builder.Property(j => j.Enabled).HasColumnName("enabled").IsRequired();
        builder.Property(j => j.MaxConcurrent).HasColumnName("max_concurrent").IsRequired();
        builder.Property(j => j.TimeoutSeconds).HasColumnName("timeout_seconds").IsRequired();
        builder.Property(j => j.LastRunAt).HasColumnName("last_run_at");
        builder.Property(j => j.NextRunAt).HasColumnName("next_run_at");
        builder.Property(j => j.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(j => j.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(j => j.ServiceId).HasDatabaseName("ix_scheduled_jobs_service");
        builder.HasIndex(j => new { j.Enabled, j.NextRunAt }).HasDatabaseName("ix_scheduled_jobs_due");

        builder.Ignore(j => j.DomainEvents);
    }

    private static ScheduledJobId ParseScheduledJobId(string s)
        => AethraId.TryParse(s, out var parsed) ? new ScheduledJobId(parsed.Value) : default;

    private static ManagedServiceId ParseManagedServiceId(string s)
        => AethraId.TryParse(s, out var parsed) ? new ManagedServiceId(parsed.Value) : default;
}

internal sealed class ScheduledJobRunConfiguration : IEntityTypeConfiguration<ScheduledJobRun>
{
    public void Configure(EntityTypeBuilder<ScheduledJobRun> builder)
    {
        builder.ToTable("scheduled_job_runs");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<ScheduledJobRunId, string>(
                id => id.ToString(),
                s => ParseScheduledJobRunId(s)))
            .HasMaxLength(64);

        builder.Property(r => r.JobId)
            .HasColumnName("job_id")
            .HasConversion(new ValueConverter<ScheduledJobId, string>(
                id => id.ToString(),
                s => ParseScheduledJobId(s)))
            .HasMaxLength(64)
            .IsRequired();

        builder.HasOne<ScheduledJob>()
            .WithMany()
            .HasForeignKey(r => r.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(r => r.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(r => r.FinishedAt).HasColumnName("finished_at");
        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(r => r.ExitCode).HasColumnName("exit_code");
        // 64KB cada uno; varchar suficiente para texto compactado.
        builder.Property(r => r.Stdout).HasColumnName("stdout").HasColumnType("text");
        builder.Property(r => r.Stderr).HasColumnName("stderr").HasColumnType("text");
        builder.Property(r => r.DurationMs).HasColumnName("duration_ms");

        builder.HasIndex(r => new { r.JobId, r.StartedAt }).HasDatabaseName("ix_scheduled_job_runs_job_time");

        builder.Ignore(r => r.DomainEvents);
    }

    private static ScheduledJobRunId ParseScheduledJobRunId(string s)
        => AethraId.TryParse(s, out var parsed) ? new ScheduledJobRunId(parsed.Value) : default;

    private static ScheduledJobId ParseScheduledJobId(string s)
        => AethraId.TryParse(s, out var parsed) ? new ScheduledJobId(parsed.Value) : default;
}
