using Aethra.Shared.Contracts.Vms;

namespace Aethra.Satellite.Buffer;

/// <summary>
/// Buffer local persistente para snapshots de métricas. Replica el patrón "replication"
/// de Netdata: si el central no es alcanzable, las muestras se persisten localmente y
/// se drenan cuando vuelve la conexión. Garantiza no perder métricas durante outages.
/// </summary>
public interface ISnapshotBuffer
{
    /// <summary>Persiste un snapshot en el buffer local con ID autoincremental.</summary>
    Task EnqueueAsync(VmMetricSnapshot snapshot, CancellationToken ct);

    /// <summary>
    /// Lee hasta <paramref name="batchSize"/> snapshots ordenados cronológicamente
    /// (más antiguos primero). No los elimina; eso lo hace <see cref="MarkSentAsync"/>.
    /// </summary>
    Task<IReadOnlyList<BufferedSnapshot>> DrainBatchAsync(int batchSize, CancellationToken ct);

    /// <summary>Elimina del buffer los snapshots ya confirmados como entregados al central.</summary>
    Task MarkSentAsync(IReadOnlyList<long> ids, CancellationToken ct);

    /// <summary>Limpieza de ring buffer: elimina entradas anteriores a un cutoff (típicamente now-24h).</summary>
    Task PruneOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct);
}

/// <summary>Snapshot leído del buffer con su ID interno para acuse de envío.</summary>
public sealed record BufferedSnapshot(long Id, VmMetricSnapshot Snapshot);
