using Aethra.Satellite.Docker;
using Aethra.Shared.Contracts.Deployments;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Aethra.Satellite.Workers;

/// <summary>
/// Registra en el <see cref="HubConnection"/> los handlers de los comandos que el central
/// invoca sobre el satélite vía SignalR (canal inverso).
/// <para>
/// Patrón: el central llama <c>connection.InvokeAsync&lt;TResult&gt;("Comando", request)</c>
/// contra <em>su lado del hub</em> (es decir, el cliente — el satélite — que tiene
/// <c>.On&lt;TReq, TResult&gt;(...)</c> registrado). Para los comandos void (Stop/Remove)
/// usamos <c>.On&lt;TReq&gt;(...)</c> y el satélite responde con un ACK vacío.
/// </para>
/// <para>
/// Para <c>StreamLogs</c> usamos <c>HubConnection.On(..., IAsyncEnumerable&lt;LogChunk&gt;)</c>
/// vía <see cref="ChannelReader{T}"/>, que SignalR mapea al cliente como un stream.
/// El central debe invocarlo con <c>connection.StreamAsync&lt;LogChunk&gt;("StreamLogs", request)</c>.
/// </para>
/// </summary>
public sealed class SatelliteCommandHandler(
    IDockerClient docker,
    ILogger<SatelliteCommandHandler> logger)
{
    /// <summary>
    /// Engancha al <paramref name="connection"/> los handlers de comandos. Debe llamarse
    /// una sola vez tras la primera conexión; los registros persisten a través de reconnects
    /// del <see cref="HubConnection"/>.
    /// </summary>
    public void Register(HubConnection connection)
    {
        // BuildImage: petición → resultado con logs y ID.
        connection.On<BuildImageRequest, BuildImageResult>("BuildImage", async (req) =>
        {
            logger.LogInformation("Central → BuildImage (job={Job}, tag={Tag})", req.BuildJobId, req.ImageTag);
            return await docker.BuildImageAsync(req, CancellationToken.None);
        });

        // RunContainer: pull (si hace falta) + create + start.
        connection.On<RunContainerRequest, RunContainerResult>("RunContainer", async (req) =>
        {
            logger.LogInformation("Central → RunContainer (job={Job}, name={Name})", req.DeployJobId, req.ContainerName);
            return await docker.RunContainerAsync(req, CancellationToken.None);
        });

        // StopContainer: comando void (sin resultado). El central usa InvokeAsync sin <TResult>.
        connection.On<StopContainerRequest>("StopContainer", async (req) =>
        {
            logger.LogInformation("Central → StopContainer (name={Name})", req.ContainerName);
            await docker.StopContainerAsync(req, CancellationToken.None);
        });

        // RemoveContainer: comando void.
        connection.On<RemoveContainerRequest>("RemoveContainer", async (req) =>
        {
            logger.LogInformation("Central → RemoveContainer (name={Name})", req.ContainerName);
            await docker.RemoveContainerAsync(req, CancellationToken.None);
        });

        // StreamLogs: el central llama StreamAsync<LogChunk>("StreamLogs", req)
        // y consume el IAsyncEnumerable que devolvemos aquí.
        connection.On<StreamLogsRequest, IAsyncEnumerable<LogChunk>>("StreamLogs", (req) =>
        {
            logger.LogInformation("Central → StreamLogs (name={Name}, follow={Follow})", req.ContainerName, req.Follow);
            return docker.StreamLogsAsync(req, CancellationToken.None);
        });

        // ListContainers: ignoramos el record vacío del request (sirve como marcador del comando).
        connection.On<ListContainersRequest, IReadOnlyList<ContainerSummary>>("ListContainers", async (_) =>
        {
            logger.LogDebug("Central → ListContainers");
            return await docker.ListContainersAsync(CancellationToken.None);
        });
    }
}
