using Aethra.Satellite.Containers;
using Aethra.Shared.Contracts.Containers;
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
/// que SignalR mapea al cliente como un stream. El central debe invocarlo con
/// <c>connection.StreamAsync&lt;LogChunk&gt;("StreamLogs", request)</c>.
/// </para>
/// </summary>
public sealed class SatelliteCommandHandler(
    IContainerRuntime runtime,
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
        connection.On<BuildImageRequest, BuildImageResponse>("BuildImage", async (req) =>
        {
            logger.LogInformation("Central → BuildImage (corr={Corr}, image={Image})",
                req.CorrelationId, req.Spec.ImageRef);
            var build = await runtime.BuildImageAsync(req.Spec, CancellationToken.None);

            // Si el central pidió push tras build y el build fue exitoso, pusheamos.
            if (build.Success && req.PushTo is { } pushAuth)
            {
                var push = await runtime.PushImageAsync(req.Spec.ImageRef, pushAuth, CancellationToken.None);
                if (!push.Success)
                {
                    var logs = build.LogLines.ToList();
                    logs.Add($"PUSH ERROR: {push.ErrorMessage}");
                    return new BuildImageResponse(
                        req.CorrelationId,
                        new BuildResult(Success: false, build.ImageId, push.ErrorMessage, logs));
                }
            }

            return new BuildImageResponse(req.CorrelationId, build);
        });

        // RunContainer: pull (si hace falta) + create + start.
        connection.On<RunContainerRequest, RunContainerResponse>("RunContainer", async (req) =>
        {
            logger.LogInformation("Central → RunContainer (corr={Corr}, name={Name})",
                req.CorrelationId, req.Spec.ContainerName);
            if (req.PullFrom is not null)
            {
                await runtime.PullImageAsync(req.Spec.ImageRef, req.PullFrom, CancellationToken.None);
            }
            var result = await runtime.RunContainerAsync(req.Spec, CancellationToken.None);
            return new RunContainerResponse(req.CorrelationId, result);
        });

        // StopContainer: comando void (sin resultado). El central usa InvokeAsync sin <TResult>.
        connection.On<StopContainerRequest>("StopContainer", async (req) =>
        {
            logger.LogInformation("Central → StopContainer (corr={Corr}, name={Name})",
                req.CorrelationId, req.ContainerNameOrId);
            await runtime.StopContainerAsync(req.ContainerNameOrId, CancellationToken.None);
        });

        // RemoveContainer: comando void.
        connection.On<RemoveContainerRequest>("RemoveContainer", async (req) =>
        {
            logger.LogInformation("Central → RemoveContainer (corr={Corr}, name={Name}, force={Force})",
                req.CorrelationId, req.ContainerNameOrId, req.Force);
            await runtime.RemoveContainerAsync(req.ContainerNameOrId, req.Force, CancellationToken.None);
        });

        // StreamLogs: el central llama StreamAsync<LogChunk>("StreamLogs", req)
        // y consume el IAsyncEnumerable que devolvemos aquí.
        connection.On<StreamLogsRequest, IAsyncEnumerable<LogChunk>>("StreamLogs", (req) =>
        {
            logger.LogInformation("Central → StreamLogs (corr={Corr}, name={Name}, tail={Tail})",
                req.CorrelationId, req.ContainerNameOrId, req.TailLines);
            return StreamLogsWithCorrelationAsync(req, CancellationToken.None);
        });

        // ListContainers.
        connection.On<ListContainersRequest, ListContainersResponse>("ListContainers", async (req) =>
        {
            logger.LogDebug("Central → ListContainers (corr={Corr})", req.CorrelationId);
            var containers = await runtime.ListContainersAsync(CancellationToken.None);
            return new ListContainersResponse(req.CorrelationId, containers);
        });
    }

    /// <summary>
    /// Envuelve el stream de líneas crudas del runtime en <see cref="LogChunk"/> con el
    /// <c>CorrelationId</c> del request original para que el central pueda multiplexar
    /// varios streams concurrentes sobre la misma conexión.
    /// </summary>
    private async IAsyncEnumerable<LogChunk> StreamLogsWithCorrelationAsync(
        StreamLogsRequest req,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var line in runtime.StreamLogsAsync(req.ContainerNameOrId, req.TailLines, ct))
        {
            yield return new LogChunk(req.CorrelationId, DateTimeOffset.UtcNow, line);
        }
    }
}
