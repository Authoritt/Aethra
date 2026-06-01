namespace Aethra.Shared.Contracts.Containers;

/// <summary>
/// Cliente RPC tipado del canal inverso central → satélite. Encapsula la lógica de
/// resolver la <c>HubConnection</c> activa para un <c>vmId</c>, enviar el comando,
/// esperar la respuesta correlacionada y mapear el resultado.
/// <para>
/// Vive en <c>Shared.Contracts.Containers</c> (no en <c>apps/api</c>) porque los módulos
/// (Deployments, Services) necesitan inyectarlo en sus orquestadores. La implementación
/// concreta sigue viviendo en el host central, que es quien posee la
/// <c>IHubContext&lt;SatelliteHub&gt;</c> de SignalR.
/// </para>
/// <para>
/// F9.8C: implementación real con correlation tracking sobre <c>IHubContext</c>
/// (<c>SignalRSatelliteRpcClient</c>). Si no hay satélite conectado lanza
/// <see cref="SatelliteNotConnectedException"/>; los orquestadores la mapean a un
/// <c>errorCode</c> estable.
/// </para>
/// </summary>
public interface ISatelliteRpcClient
{
    /// <summary>Envía un <c>BuildImageRequest</c> a la VM indicada y espera la respuesta.</summary>
    Task<BuildResult> SendBuildAsync(
        string vmId, BuildSpec spec, RegistryAuth? pushTo, CancellationToken ct);

    /// <summary>Envía un <c>RunContainerRequest</c> a la VM indicada y espera la respuesta.</summary>
    Task<RunResult> SendRunAsync(
        string vmId, RunSpec spec, RegistryAuth? pullFrom, CancellationToken ct);

    /// <summary>Detiene un contenedor en la VM indicada.</summary>
    Task SendStopAsync(string vmId, string containerNameOrId, CancellationToken ct);

    /// <summary>Elimina un contenedor en la VM indicada.</summary>
    Task SendRemoveAsync(string vmId, string containerNameOrId, bool force, CancellationToken ct);

    /// <summary>Solicita el stream de logs de un contenedor. Cada elemento del <see cref="IAsyncEnumerable{T}"/>
    /// es una línea de log con timestamp.</summary>
    IAsyncEnumerable<LogChunk> StreamLogsAsync(
        string vmId, string containerNameOrId, int tailLines, CancellationToken ct);

    /// <summary>Lista los contenedores conocidos por el satélite de la VM indicada.</summary>
    Task<IReadOnlyList<ContainerInfo>> SendListContainersAsync(string vmId, CancellationToken ct);
}
