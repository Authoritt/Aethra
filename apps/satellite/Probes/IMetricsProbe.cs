using Aethra.Shared.Contracts.Vms;

namespace Aethra.Satellite.Probes;

/// <summary>
/// Abstracción del recolector de métricas del sistema operativo.
/// Implementaciones: <see cref="LinuxMetricsProbe"/> (lee /proc) y
/// <see cref="CrossPlatformMetricsProbe"/> (fallback con BCL para Windows en dev).
/// </summary>
public interface IMetricsProbe
{
    /// <summary>
    /// Información estática del host (CPU model, cores, RAM total, kernel).
    /// Se reporta una sola vez en el handshake.
    /// </summary>
    Task<SatelliteHandshake> HandshakeAsync(CancellationToken ct);

    /// <summary>
    /// Snapshot de métricas actuales. Llamado cada N segundos por el worker.
    /// </summary>
    Task<VmMetricSnapshot> SnapshotAsync(CancellationToken ct);
}
