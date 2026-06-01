namespace Aethra.Shared.Contracts.Containers;

/// <summary>
/// Registry compartido entre el <c>SatelliteHub</c> (que registra/des-registra al conectarse y
/// desconectarse) y <see cref="ISatelliteRpcClient"/> (que consulta antes de enviar comandos).
/// La implementación concreta vive en <c>apps/api/Hubs</c> y se inyecta como singleton.
/// <para>
/// Vive en <c>Shared.Contracts</c> para romper la dependencia direccional: tanto el módulo
/// <c>Vms</c> (host del hub) como el módulo <c>Deployments</c> (host de orquestadores que pueden
/// querer descubrir VMs conectadas) lo referencian.
/// </para>
/// </summary>
public interface ISatelliteConnectionRegistry
{
    /// <summary>Registra una conexión activa para el <paramref name="vmId"/>.
    /// Si ya había una connection registrada, la reemplaza (último wins — el satélite reconectó).</summary>
    void Register(string vmId, string connectionId);

    /// <summary>Quita el registro de <paramref name="vmId"/> si la <paramref name="connectionId"/>
    /// coincide con la registrada. Si no coincide (otro satélite ya tomó el slot), no hace nada.</summary>
    void Unregister(string vmId, string connectionId);

    /// <summary>True si <paramref name="vmId"/> tiene un satélite conectado en este momento.</summary>
    bool IsConnected(string vmId);

    /// <summary>Devuelve el <c>ConnectionId</c> SignalR del satélite para <paramref name="vmId"/>,
    /// o null si no está conectado.</summary>
    string? GetConnectionId(string vmId);

    /// <summary>Snapshot de todas las VMs con satélite conectado. Útil para orquestadores que
    /// necesitan descubrir un satélite cualquiera (p.ej. BuildOrchestrator antes de F9.4 de
    /// routing por afinidad).</summary>
    IReadOnlyCollection<string> ConnectedVmIds { get; }
}
