using Aethra.Modules.Metrics.Domain;
using Aethra.Shared.Infrastructure.Persistence;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Metrics.Infrastructure;

public sealed class MetricsDbContext(DbContextOptions<MetricsDbContext> options) : AethraModuleDbContext(options)
{
    public override string SchemaName => "metrics";

    public DbSet<VmMetricRecord> VmMetrics => Set<VmMetricRecord>();
    public DbSet<ContainerSnapshotRecord> ContainerSnapshots => Set<ContainerSnapshotRecord>();

    // Helpers estáticos (clase): los expression trees de EF no aceptan funciones locales (CS8110).
    private static VmMetricId ParseVmMetricId(string s)
        => AethraId.TryParse(s, out var p) ? new VmMetricId(p.Value) : default;

    private static ContainerSnapshotId ParseContainerSnapshotId(string s)
        => AethraId.TryParse(s, out var p) ? new ContainerSnapshotId(p.Value) : default;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var vmMetricIdConv = new ValueConverter<VmMetricId, string>(
            id => id.ToString(),
            s => ParseVmMetricId(s));

        var containerSnapshotIdConv = new ValueConverter<ContainerSnapshotId, string>(
            id => id.ToString(),
            s => ParseContainerSnapshotId(s));

        modelBuilder.Entity<VmMetricRecord>(b =>
        {
            b.ToTable("vm_metrics");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasConversion(vmMetricIdConv).HasMaxLength(64);
            b.Property(x => x.VmId).HasColumnName("vm_id").HasMaxLength(64).IsRequired();
            b.Property(x => x.Timestamp).HasColumnName("timestamp").IsRequired();
            b.Property(x => x.CpuPercent).HasColumnName("cpu_percent");
            b.Property(x => x.LoadAverage1).HasColumnName("load_1");
            b.Property(x => x.LoadAverage5).HasColumnName("load_5");
            b.Property(x => x.LoadAverage15).HasColumnName("load_15");
            b.Property(x => x.MemoryUsedBytes).HasColumnName("mem_used");
            b.Property(x => x.MemoryFreeBytes).HasColumnName("mem_free");
            b.Property(x => x.MemoryTotalBytes).HasColumnName("mem_total");
            b.Property(x => x.SwapUsedBytes).HasColumnName("swap_used");
            b.Property(x => x.SwapTotalBytes).HasColumnName("swap_total");
            b.Property(x => x.DisksJson).HasColumnName("disks").HasColumnType("jsonb");
            b.Property(x => x.NetBytesReceived).HasColumnName("net_bytes_rx");
            b.Property(x => x.NetBytesSent).HasColumnName("net_bytes_tx");
            b.Property(x => x.NetPacketsReceived).HasColumnName("net_packets_rx");
            b.Property(x => x.NetPacketsSent).HasColumnName("net_packets_tx");
            b.Property(x => x.UptimeSeconds).HasColumnName("uptime_seconds");

            // Index time-series: por VM y timestamp descendente para queries "últimas N muestras".
            b.HasIndex(x => new { x.VmId, x.Timestamp })
                .HasDatabaseName("ix_vm_metrics_vm_time")
                .IsDescending(false, true);
        });

        modelBuilder.Entity<ContainerSnapshotRecord>(b =>
        {
            b.ToTable("container_snapshots");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasConversion(containerSnapshotIdConv).HasMaxLength(64);
            b.Property(x => x.VmId).HasColumnName("vm_id").HasMaxLength(64).IsRequired();
            b.Property(x => x.Timestamp).HasColumnName("timestamp").IsRequired();
            b.Property(x => x.ContainerCount).HasColumnName("container_count");
            b.Property(x => x.ContainersJson).HasColumnName("containers").HasColumnType("jsonb");

            b.HasIndex(x => new { x.VmId, x.Timestamp })
                .HasDatabaseName("ix_container_snapshots_vm_time")
                .IsDescending(false, true);
        });
    }
}
