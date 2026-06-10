namespace Aethra.Shared.Contracts.Vms;

/// <summary>
/// Snapshot que el satélite envía periódicamente vía SignalR.
/// Está en Shared.Contracts porque tanto el satélite (cliente) como el central (hub)
/// y otros módulos (Metrics) lo deserializan.
/// </summary>
public sealed record SatelliteHandshake(
    string Hostname,
    string KernelVersion,
    string CpuModel,
    int CpuCores,
    long TotalMemoryBytes,
    string AgentVersion,
    string? ContainerRuntime = null,
    long? RootDiskTotalBytes = null,
    long? RootDiskAvailableBytes = null);

public sealed record VmMetricSnapshot(
    DateTimeOffset Timestamp,
    double CpuPercent,
    double LoadAverage1,
    double LoadAverage5,
    double LoadAverage15,
    long MemoryUsedBytes,
    long MemoryFreeBytes,
    long MemoryTotalBytes,
    long SwapUsedBytes,
    long SwapTotalBytes,
    IReadOnlyList<DiskUsage> Disks,
    NetworkSnapshot Network,
    double UptimeSeconds);

public sealed record DiskUsage(
    string MountPoint,
    string Filesystem,
    long TotalBytes,
    long UsedBytes,
    long AvailableBytes);

public sealed record NetworkSnapshot(
    long BytesReceived,
    long BytesSent,
    long PacketsReceived,
    long PacketsSent);

public sealed record ContainerListSnapshot(
    DateTimeOffset Timestamp,
    IReadOnlyList<ContainerInfo> Containers);

public sealed record ContainerInfo(
    string Id,
    string Name,
    string Image,
    string Status,
    string State,
    DateTimeOffset CreatedAt,
    IReadOnlyList<int> Ports);
