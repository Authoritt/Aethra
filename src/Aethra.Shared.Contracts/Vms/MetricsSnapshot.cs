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
    string? ContainerRuntimeVersion = null,
    long? RootDiskTotalBytes = null,
    long? RootDiskAvailableBytes = null,
    bool? RuntimeSocketAccessible = null,
    string? DataVolumePath = null,
    long? DataVolumeTotalBytes = null,
    long? DataVolumeAvailableBytes = null);

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

/// <summary>
/// Un contenedor del host con sus stats de uso. El satélite lista TODOS los contenedores
/// (gestionados por Aethra o no) y para los que están corriendo adjunta CPU/memoria/red/disco/IO.
/// Los campos de stats son nullable: un contenedor detenido (o un runtime que no los provee, p.ej.
/// Podman parcial) los deja en null y la UI degrada con guiones. Se serializa como JSONB en
/// <c>metrics.container_snapshots</c> → añadir campos NO requiere migración.
/// </summary>
public sealed record ContainerInfo(
    string Id,
    string Name,
    string Image,
    string Status,
    string State,
    DateTimeOffset CreatedAt,
    IReadOnlyList<int> Ports,
    // Stats de uso (null si el contenedor no corre o el runtime no las expone).
    double? CpuPercent = null,
    long? MemoryUsedBytes = null,
    long? MemoryLimitBytes = null,
    long? NetRxBytes = null,
    long? NetTxBytes = null,
    long? BlockReadBytes = null,
    long? BlockWriteBytes = null,
    // Disco del contenedor: capa escribible (SizeRw) y total incl. imagen (SizeRootFs).
    long? SizeRwBytes = null,
    long? SizeRootFsBytes = null);
