using Aethra.Satellite.Containers;
using Aethra.Shared.Contracts.Containers;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aethra.Satellite.Workers;

/// <summary>
/// Registra en el <see cref="HubConnection"/> los handlers de los comandos que el central
/// invoca sobre el satélite vía SignalR (canal inverso).
/// <para>
/// Patrón (F9.8C): el central manda un <c>SendAsync("BuildImage", req)</c> (fire-and-forget)
/// con un <c>CorrelationId</c>; el satélite ejecuta el comando contra <see cref="IContainerRuntime"/>
/// y devuelve la respuesta invocando un método del hub (<c>BuildImageResponse</c>,
/// <c>RpcFailed</c>, <c>LogChunkPush</c>, etc.) que correlaciona contra el
/// <c>TaskCompletionSource</c> pendiente en el central. Si el runtime lanza, capturamos y
/// reportamos <c>RpcFailed</c> al central — el satélite NO se desconecta del hub.
/// </para>
/// </summary>
public sealed class SatelliteCommandHandler(
    IContainerRuntime runtime,
    ILogger<SatelliteCommandHandler> logger,
    IOptions<SatelliteOptions> options)
{
    private readonly int _imageRetentionKeep = options.Value.ImageRetentionKeep;

    /// <summary>Deriva el repositorio (sin el <c>:tag</c>) de un image ref, respetando un puerto
    /// de registry (<c>host:5000/repo:tag</c>): el separador es el último ':' después del último '/'.</summary>
    private static string ImageRepoOf(string imageRef)
    {
        var lastSlash = imageRef.LastIndexOf('/');
        var lastColon = imageRef.LastIndexOf(':');
        return lastColon > lastSlash ? imageRef[..lastColon] : imageRef;
    }

    /// <summary>
    /// Engancha al <paramref name="connection"/> los handlers de comandos. Debe llamarse
    /// una sola vez tras la primera conexión; los registros persisten a través de reconnects
    /// del <see cref="HubConnection"/>.
    /// </summary>
    public void Register(HubConnection connection)
    {
        // BuildImage: build + optional push. La respuesta se invoca explícitamente al hub
        // como `BuildImageResponse`; en error invocamos `RpcFailed`.
        connection.On<BuildImageRequest>("BuildImage", async (req) =>
        {
            await HandleAsync(connection, req.CorrelationId, "BuildImage", async () =>
            {
                logger.LogInformation("Central → BuildImage (corr={Corr}, image={Image})",
                    req.CorrelationId, req.Spec.ImageRef);
                var build = await runtime.BuildImageAsync(req.Spec, CancellationToken.None);

                // Push opcional tras build exitoso.
                if (build.Success && req.PushTo is { } pushAuth)
                {
                    var push = await runtime.PushImageAsync(req.Spec.ImageRef, pushAuth, CancellationToken.None);
                    if (!push.Success)
                    {
                        var logs = build.LogLines.ToList();
                        logs.Add($"PUSH ERROR: {push.ErrorMessage}");
                        build = new BuildResult(Success: false, build.ImageId, push.ErrorMessage, logs);
                    }
                }

                await connection.InvokeAsync(
                    "BuildImageResponse",
                    new BuildImageResponse(req.CorrelationId, build),
                    CancellationToken.None);

                // Retención: tras un build exitoso, purgar tags viejos del mismo repo para que los
                // flujos git-mode (un tag por commit) no saturen el disco. Best-effort: corre después
                // de responder y nunca falla el build ni borra imágenes en uso.
                if (build.Success && _imageRetentionKeep > 0)
                {
                    var repo = ImageRepoOf(req.Spec.ImageRef);
                    try
                    {
                        var removed = await runtime.PruneImageRepoAsync(repo, _imageRetentionKeep, CancellationToken.None);
                        if (removed.Count > 0)
                        {
                            logger.LogInformation(
                                "Retención de imágenes: borrados {Count} tags viejos de {Repo} (keep={Keep}).",
                                removed.Count, repo, _imageRetentionKeep);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Retención de imágenes falló para {Repo} (no bloquea el build).", repo);
                    }
                }
            });
        });

        // RunContainer: pull (si hace falta) + create + start.
        connection.On<RunContainerRequest>("RunContainer", async (req) =>
        {
            await HandleAsync(connection, req.CorrelationId, "RunContainer", async () =>
            {
                logger.LogInformation("Central → RunContainer (corr={Corr}, name={Name})",
                    req.CorrelationId, req.Spec.ContainerName);
                if (req.PullFrom is not null)
                {
                    await runtime.PullImageAsync(req.Spec.ImageRef, req.PullFrom, CancellationToken.None);
                }
                var result = await runtime.RunContainerAsync(req.Spec, CancellationToken.None);
                await connection.InvokeAsync(
                    "RunContainerResponse",
                    new RunContainerResponse(req.CorrelationId, result),
                    CancellationToken.None);
            });
        });

        // StopContainer: ack vacío en éxito.
        connection.On<StopContainerRequest>("StopContainer", async (req) =>
        {
            await HandleAsync(connection, req.CorrelationId, "StopContainer", async () =>
            {
                logger.LogInformation("Central → StopContainer (corr={Corr}, name={Name})",
                    req.CorrelationId, req.ContainerNameOrId);
                await runtime.StopContainerAsync(req.ContainerNameOrId, CancellationToken.None);
                await connection.InvokeAsync(
                    "StopContainerAck", req.CorrelationId, CancellationToken.None);
            });
        });

        // RestartContainer: ack vacio en exito.
        connection.On<RestartContainerRequest>("RestartContainer", async (req) =>
        {
            await HandleAsync(connection, req.CorrelationId, "RestartContainer", async () =>
            {
                logger.LogInformation("Central -> RestartContainer (corr={Corr}, name={Name})",
                    req.CorrelationId, req.ContainerNameOrId);
                await runtime.RestartContainerAsync(req.ContainerNameOrId, CancellationToken.None);
                await connection.InvokeAsync(
                    "RestartContainerAck", req.CorrelationId, CancellationToken.None);
            });
        });

        // RemoveContainer: ack vacío en éxito.
        connection.On<RemoveContainerRequest>("RemoveContainer", async (req) =>
        {
            await HandleAsync(connection, req.CorrelationId, "RemoveContainer", async () =>
            {
                logger.LogInformation("Central → RemoveContainer (corr={Corr}, name={Name}, force={Force})",
                    req.CorrelationId, req.ContainerNameOrId, req.Force);
                await runtime.RemoveContainerAsync(req.ContainerNameOrId, req.Force, CancellationToken.None);
                await connection.InvokeAsync(
                    "RemoveContainerAck", req.CorrelationId, CancellationToken.None);
            });
        });

        // StreamLogs: por cada línea del runtime invocamos LogChunkPush; al cerrarse el stream,
        // invocamos LogStreamCompleted con un null errorMessage en éxito.
        connection.On<StreamLogsRequest>("StreamLogs", async (req) =>
        {
            logger.LogInformation("Central → StreamLogs (corr={Corr}, name={Name}, tail={Tail})",
                req.CorrelationId, req.ContainerNameOrId, req.TailLines);
            string? errorMessage = null;
            try
            {
                await foreach (var line in runtime.StreamLogsAsync(
                    req.ContainerNameOrId, req.TailLines, CancellationToken.None))
                {
                    await connection.InvokeAsync(
                        "LogChunkPush",
                        new LogChunk(req.CorrelationId, DateTimeOffset.UtcNow, line),
                        CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "StreamLogs falló (corr={Corr})", req.CorrelationId);
                errorMessage = $"{ex.GetType().Name}: {ex.Message}";
            }
            try
            {
                await connection.InvokeAsync(
                    "LogStreamCompleted", req.CorrelationId, errorMessage, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo notificar LogStreamCompleted (corr={Corr})", req.CorrelationId);
            }
        });

        // ListContainers.
        connection.On<ListContainersRequest>("ListContainers", async (req) =>
        {
            await HandleAsync(connection, req.CorrelationId, "ListContainers", async () =>
            {
                logger.LogDebug("Central → ListContainers (corr={Corr})", req.CorrelationId);
                var containers = await runtime.ListContainersAsync(CancellationToken.None);
                await connection.InvokeAsync(
                    "ListContainersResponse",
                    new ListContainersResponse(req.CorrelationId, containers),
                    CancellationToken.None);
            });
        });

        // F12.1A — ExecInContainer.
        connection.On<ExecInContainerRequest>("ExecInContainer", async (req) =>
        {
            await HandleAsync(connection, req.CorrelationId, "ExecInContainer", async () =>
            {
                logger.LogInformation("Central → ExecInContainer (corr={Corr}, container={Name})",
                    req.CorrelationId, req.ContainerNameOrId);
                var result = await runtime.ExecInContainerAsync(
                    req.ContainerNameOrId, req.Command, req.TimeoutSeconds, CancellationToken.None);
                await connection.InvokeAsync(
                    "ExecInContainerResponse",
                    new ExecInContainerResponse(req.CorrelationId, result),
                    CancellationToken.None);
            });
        });
    }

    /// <summary>
    /// Envuelve la ejecución de <paramref name="action"/> con captura de excepciones del runtime.
    /// En error, intenta notificar al central con <c>RpcFailed</c> y NO desconecta el hub.
    /// </summary>
    private async Task HandleAsync(
        HubConnection connection, string correlationId, string method, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Comando {Method} (corr={Corr}) falló en el satélite", method, correlationId);
            try
            {
                var errorMessage = $"runtime_unavailable: {ex.GetType().Name}: {ex.Message}";
                await connection.InvokeAsync(
                    "RpcFailed", correlationId, errorMessage, CancellationToken.None);
            }
            catch (Exception notifyEx)
            {
                logger.LogWarning(notifyEx,
                    "No se pudo notificar RpcFailed al central (corr={Corr}); el central caerá en timeout.",
                    correlationId);
            }
        }
    }
}
