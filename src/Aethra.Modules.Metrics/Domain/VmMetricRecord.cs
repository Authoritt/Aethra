using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Metrics.Domain;

/// <summary>
/// Una muestra de métricas de una VM en un instante. Time-series:
/// índice (vm_id, timestamp DESC). Retención inicial: 24h crudo, downsample en F3+.
/// </summary>
public sealed class VmMetricRecord : Entity<VmMetricId>
{
    public string VmId { get; private set; }    // string en lugar de VmId typed porque otro módulo
    public DateTimeOffset Timestamp { get; private set; }
    public double CpuPercent { get; private set; }
    public double LoadAverage1 { get; private set; }
    public double LoadAverage5 { get; private set; }
    public double LoadAverage15 { get; private set; }
    public long MemoryUsedBytes { get; private set; }
    public long MemoryFreeBytes { get; private set; }
    public long MemoryTotalBytes { get; private set; }
    public long SwapUsedBytes { get; private set; }
    public long SwapTotalBytes { get; private set; }
    public string DisksJson { get; private set; }       // JSON serializado de IReadOnlyList<DiskUsage>
    public long NetBytesReceived { get; private set; }
    public long NetBytesSent { get; private set; }
    public long NetPacketsReceived { get; private set; }
    public long NetPacketsSent { get; private set; }
    public double UptimeSeconds { get; private set; }

    public static VmMetricRecord FromSnapshot(string vmId, Aethra.Shared.Contracts.Vms.VmMetricSnapshot s)
    {
        return new VmMetricRecord
        {
            Id = VmMetricId.New(),
            VmId = vmId,
            Timestamp = s.Timestamp,
            CpuPercent = s.CpuPercent,
            LoadAverage1 = s.LoadAverage1,
            LoadAverage5 = s.LoadAverage5,
            LoadAverage15 = s.LoadAverage15,
            MemoryUsedBytes = s.MemoryUsedBytes,
            MemoryFreeBytes = s.MemoryFreeBytes,
            MemoryTotalBytes = s.MemoryTotalBytes,
            SwapUsedBytes = s.SwapUsedBytes,
            SwapTotalBytes = s.SwapTotalBytes,
            DisksJson = System.Text.Json.JsonSerializer.Serialize(s.Disks),
            NetBytesReceived = s.Network.BytesReceived,
            NetBytesSent = s.Network.BytesSent,
            NetPacketsReceived = s.Network.PacketsReceived,
            NetPacketsSent = s.Network.PacketsSent,
            UptimeSeconds = s.UptimeSeconds,
        };
    }

    // EF Core
    private VmMetricRecord()
    {
        VmId = string.Empty;
        DisksJson = "[]";
    }
}
