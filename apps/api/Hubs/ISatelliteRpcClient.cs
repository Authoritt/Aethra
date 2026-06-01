using Aethra.Shared.Contracts.Containers;

namespace Aethra.Api.Hubs;

/// <summary>
/// Cliente RPC tipado del canal inverso central → satélite. Encapsula la lógica de
/// resolver la <c>HubConnection</c> activa para un <c>vmId</c>, enviar el comando,
/// esperar la respuesta correlacionada y mapear el resultado.
/// <para>
/// STUB de F9.2: las implementaciones reales con correlation tracking sobre
/// <see cref="Microsoft.AspNetCore.SignalR.IHubContext{T}"/> se cablearán en F9.3.
/// Por ahora cualquier llamada lanza <see cref="NotImplementedException"/>.
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
