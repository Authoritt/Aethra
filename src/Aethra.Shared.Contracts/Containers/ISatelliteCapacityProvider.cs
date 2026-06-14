namespace Aethra.Shared.Contracts.Containers;

/// <summary>Capacidad de disco de un satélite, para elegir dónde colocar blobs (backups, etc.).</summary>
public sealed record SatelliteCapacity(string VmId, string Slug, long? FreeBytes, bool Connected);

/// <summary>
/// Provee la capacidad de disco de los satélites conocidos. Vive en <c>Shared.Contracts</c> (como
/// <see cref="ISatelliteRpcClient"/>) para que los módulos lo inyecten sin depender del módulo Vms;
/// la implementación concreta vive en el host central (que sí accede al read model de VMs).
/// </summary>
public interface ISatelliteCapacityProvider
{
    /// <summary>Lista los satélites con su disco libre (root) y si están conectados.</summary>
    Task<IReadOnlyList<SatelliteCapacity>> GetSatellitesAsync(CancellationToken ct);
}
