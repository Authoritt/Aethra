using System.Collections.Concurrent;
using Aethra.Shared.Contracts.Containers;

namespace Aethra.Api.Hubs;

/// <summary>
/// Implementación in-memory de <see cref="ISatelliteConnectionRegistry"/>. Mantiene un
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> vmId → connectionId.
/// <para>
/// Single-host: este registry es local a este nodo del central. F9.x cuando haya múltiples
/// instancias de Aethra detrás de un load balancer habrá que cambiarlo por un store distribuido
/// (Redis backplane) o forzar sticky sessions por vmId.
/// </para>
/// </summary>
public sealed class SatelliteConnectionRegistry : ISatelliteConnectionRegistry
{
    private readonly ConcurrentDictionary<string, string> _connections = new(StringComparer.Ordinal);

    public void Register(string vmId, string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vmId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        _connections[vmId] = connectionId;
    }

    public void Unregister(string vmId, string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vmId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        // Compare-and-remove: solo borramos si el connectionId coincide. Si el satélite ya
        // reconectó con otro id, no debemos quitarlo (race entre OnDisconnected del anterior
        // y OnConnected del nuevo).
        if (_connections.TryGetValue(vmId, out var current) && current == connectionId)
        {
            _connections.TryRemove(KeyValuePair.Create(vmId, connectionId));
        }
    }

    public bool IsConnected(string vmId) => _connections.ContainsKey(vmId);

    public string? GetConnectionId(string vmId)
        => _connections.TryGetValue(vmId, out var conn) ? conn : null;

    public IReadOnlyCollection<string> ConnectedVmIds => _connections.Keys.ToArray();
}
