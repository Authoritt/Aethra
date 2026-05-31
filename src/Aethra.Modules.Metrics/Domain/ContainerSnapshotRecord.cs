using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Metrics.Domain;

/// <summary>
/// Snapshot del listado de contenedores Docker de una VM en un instante.
/// Almacenamos como JSON porque el shape de Docker varía y rara vez se filtran por columnas.
/// </summary>
public sealed class ContainerSnapshotRecord : Entity<ContainerSnapshotId>
{
    public string VmId { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }
    public int ContainerCount { get; private set; }
    public string ContainersJson { get; private set; }

    public static ContainerSnapshotRecord FromSnapshot(string vmId, Aethra.Shared.Contracts.Vms.ContainerListSnapshot s)
    {
        return new ContainerSnapshotRecord
        {
            Id = ContainerSnapshotId.New(),
            VmId = vmId,
            Timestamp = s.Timestamp,
            ContainerCount = s.Containers.Count,
            ContainersJson = System.Text.Json.JsonSerializer.Serialize(s.Containers),
        };
    }

    private ContainerSnapshotRecord()
    {
        VmId = string.Empty;
        ContainersJson = "[]";
    }
}
