using Aethra.Modules.Monitoring.Domain;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Monitoring.Infrastructure.Configurations;

internal sealed class MonitorCheckConfiguration : IEntityTypeConfiguration<MonitorCheck>
{
    public void Configure(EntityTypeBuilder<MonitorCheck> builder)
    {
        builder.ToTable("monitor_checks");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<MonitorCheckId, string>(
                id => id.ToString(),
                s => ParseMonitorCheckId(s)))
            .HasMaxLength(64);

        builder.Property(c => c.MonitorId)
            .HasColumnName("monitor_id")
            .HasConversion(new ValueConverter<MonitorId, string>(
                id => id.ToString(),
                s => ParseMonitorId(s)))
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(c => c.Timestamp).HasColumnName("timestamp").IsRequired();
        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(c => c.HttpStatusCode).HasColumnName("http_status_code");
        builder.Property(c => c.LatencyMs).HasColumnName("latency_ms");
        builder.Property(c => c.ErrorMessage).HasColumnName("error_message").HasMaxLength(1000);
        builder.Property(c => c.ResponseSnippet).HasColumnName("response_snippet").HasMaxLength(MonitorCheck.SnippetMaxLength);

        // Index time-series: por monitor y timestamp descendente para "últimos N checks".
        builder.HasIndex(c => new { c.MonitorId, c.Timestamp })
            .HasDatabaseName("ix_monitor_checks_monitor_time")
            .IsDescending(false, true);
    }

    private static MonitorId ParseMonitorId(string s)
        => AethraId.TryParse(s, out var parsed) ? new MonitorId(parsed.Value) : default;

    private static MonitorCheckId ParseMonitorCheckId(string s)
        => AethraId.TryParse(s, out var parsed) ? new MonitorCheckId(parsed.Value) : default;
}
