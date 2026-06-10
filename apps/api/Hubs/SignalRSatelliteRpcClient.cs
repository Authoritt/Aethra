using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Aethra.Modules.Vms.Presentation;
using Aethra.Shared.Contracts.Containers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aethra.Api.Hubs;

/// <summary>
/// Implementación de <see cref="ISatelliteRpcClient"/> sobre SignalR. Encapsula:
/// <list type="bullet">
///   <item>Generación de <c>CorrelationId</c> único por llamada.</item>
///   <item>Despacho del mensaje al grupo <c>vm:{vmId}</c> del <see cref="SatelliteHub"/>.</item>
///   <item>Registro del <see cref="TaskCompletionSource{TResult}"/> pendiente en un diccionario
///   concurrente para que el handler de respuesta del hub pueda resolverlo.</item>
///   <item>Timeout por defecto configurable vía <c>Satellite:RpcTimeoutSeconds</c> (default 600).</item>
///   <item>Para streams (logs) un <see cref="Channel{T}"/> separado consumido como
///   <see cref="IAsyncEnumerable{T}"/> hasta que el satélite envíe una sentinela de fin.</item>
/// </list>
/// <para>
/// El <see cref="SatelliteHub"/> debe exponer handlers que el satélite invoca con la respuesta
/// (p. ej. <c>BuildImageResponse</c>) y cuyo cuerpo llama a <see cref="CompleteRequest"/> /
/// <see cref="PushLogChunk"/> / <see cref="CompleteStream"/> sobre esta instancia. Se inyecta
/// como singleton para que ambos extremos (orquestadores y hub) compartan el diccionario de
/// requests pendientes.
/// </para>
/// </summary>
public sealed class SignalRSatelliteRpcClient : ISatelliteRpcClient, ISatelliteRpcCallbacks
{
    private readonly IHubContext<SatelliteHub> _hub;
    private readonly ISatelliteConnectionRegistry _registry;
    private readonly ILogger<SignalRSatelliteRpcClient> _logger;
    private readonly TimeSpan _defaultTimeout;
    private readonly TimeSpan _buildTimeout;
    private readonly TimeSpan _streamChunkTimeout;

    /// <summary>Requests Task-based (build, run, stop, remove, list). Resueltas en <see cref="CompleteRequest"/>.</summary>
    private readonly ConcurrentDictionary<string, TaskCompletionSource<object?>> _pending = new();

    /// <summary>Streams de logs activos. Cada canal recibe <see cref="LogChunk"/> hasta <see cref="CompleteStream"/>.</summary>
    private readonly ConcurrentDictionary<string, Channel<LogChunk>> _streams = new();

    public SignalRSatelliteRpcClient(
        IHubContext<SatelliteHub> hub,
        ISatelliteConnectionRegistry registry,
        IConfiguration cfg,
        ILogger<SignalRSatelliteRpcClient> logger)
    {
        _hub = hub;
        _registry = registry;
        _logger = logger;
        // Default timeout corto (60s) para Stop/Remove/Run/List/Push/Pull. Build se sobreescribe
        // arriba porque clonar + buildear puede tardar varios minutos. Configurable vía
        // Satellite:RpcTimeoutSeconds (default) y Satellite:BuildTimeoutSeconds (builds).
        _defaultTimeout = TimeSpan.FromSeconds(cfg.GetValue("Satellite:RpcTimeoutSeconds", 60));
        _buildTimeout = TimeSpan.FromSeconds(cfg.GetValue("Satellite:BuildTimeoutSeconds", 900));
        // Watchdog del StreamLogs: si no llega un nuevo chunk en N segundos asumimos que el
        // satélite murió sin enviar LogStreamCompleted y abortamos el `await foreach`.
        // Configurable vía Satellite:StreamChunkTimeoutSeconds (default 60s).
        _streamChunkTimeout = TimeSpan.FromSeconds(cfg.GetValue("Satellite:StreamChunkTimeoutSeconds", 60));
    }

    // -------------------------------------------------------------------------
    // ISatelliteRpcClient — operaciones Task-based.
    // -------------------------------------------------------------------------

    public async Task<BuildResult> SendBuildAsync(
        string vmId, BuildSpec spec, RegistryAuth? pushTo, CancellationToken ct)
    {
        var correlationId = NewCorrelationId();
        var resp = await SendAndAwaitAsync<BuildImageResponse>(
            vmId, correlationId, "BuildImage",
            new BuildImageRequest(correlationId, spec, pushTo), _buildTimeout, ct).ConfigureAwait(false);
        return resp.Result;
    }

    public async Task<RunResult> SendRunAsync(
        string vmId, RunSpec spec, RegistryAuth? pullFrom, CancellationToken ct)
    {
        var correlationId = NewCorrelationId();
        var resp = await SendAndAwaitAsync<RunContainerResponse>(
            vmId, correlationId, "RunContainer",
            new RunContainerRequest(correlationId, spec, pullFrom), _defaultTimeout, ct).ConfigureAwait(false);
        return resp.Result;
    }

    public async Task SendStopAsync(string vmId, string containerNameOrId, CancellationToken ct)
    {
        var correlationId = NewCorrelationId();
        await SendAndAwaitAsync<object>(
            vmId, correlationId, "StopContainer",
            new StopContainerRequest(correlationId, containerNameOrId), _defaultTimeout, ct).ConfigureAwait(false);
    }

    public async Task SendRestartAsync(string vmId, string containerNameOrId, CancellationToken ct)
    {
        var correlationId = NewCorrelationId();
        await SendAndAwaitAsync<object>(
            vmId, correlationId, "RestartContainer",
            new RestartContainerRequest(correlationId, containerNameOrId), _defaultTimeout, ct).ConfigureAwait(false);
    }

    public async Task SendRemoveAsync(
        string vmId, string containerNameOrId, bool force, CancellationToken ct)
    {
        var correlationId = NewCorrelationId();
        await SendAndAwaitAsync<object>(
            vmId, correlationId, "RemoveContainer",
            new RemoveContainerRequest(correlationId, containerNameOrId, force), _defaultTimeout, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ContainerInfo>> SendListContainersAsync(
        string vmId, CancellationToken ct)
    {
        var correlationId = NewCorrelationId();
        var resp = await SendAndAwaitAsync<ListContainersResponse>(
            vmId, correlationId, "ListContainers",
            new ListContainersRequest(correlationId), _defaultTimeout, ct).ConfigureAwait(false);
        return resp.Containers;
    }

    public async Task<ExecResult> SendExecAsync(
        string vmId, string containerNameOrId, string command, int timeoutSeconds, CancellationToken ct)
    {
        var correlationId = NewCorrelationId();
        // El timeout en el cliente RPC debe superar al timeout del exec en el satelite,
        // para que el satelite tenga tiempo de matar el proceso y responder con TimedOut=true
        // antes de que el cliente caiga en su propio timeout.
        var clientTimeout = TimeSpan.FromSeconds(Math.Max(timeoutSeconds + 30, (int)_defaultTimeout.TotalSeconds));
        var resp = await SendAndAwaitAsync<ExecInContainerResponse>(
            vmId, correlationId, "ExecInContainer",
            new ExecInContainerRequest(correlationId, containerNameOrId, command, timeoutSeconds),
            clientTimeout, ct).ConfigureAwait(false);
        return resp.Result;
    }

    // -------------------------------------------------------------------------
    // ISatelliteRpcClient — streams.
    // -------------------------------------------------------------------------

    public async IAsyncEnumerable<LogChunk> StreamLogsAsync(
        string vmId, string containerNameOrId, int tailLines,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var connectionId = _registry.GetConnectionId(vmId)
            ?? throw new SatelliteNotConnectedException(vmId);

        var correlationId = NewCorrelationId();
        var channel = Channel.CreateUnbounded<LogChunk>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        if (!_streams.TryAdd(correlationId, channel))
        {
            throw new InvalidOperationException(
                $"Colisión inesperada de correlationId={correlationId} en streams.");
        }

        try
        {
            var req = new StreamLogsRequest(correlationId, containerNameOrId, tailLines);
            await _hub.Clients.Client(connectionId)
                .SendAsync("StreamLogs", req, ct).ConfigureAwait(false);

            // Watchdog por chunk: si pasa _streamChunkTimeout sin recibir nada del satélite,
            // asumimos que murió silenciosamente (sin enviar LogStreamCompleted) y abortamos
            // el await foreach con un TimeoutException. Se rearma cada vez que llega un chunk.
            // No podemos hacer try/catch alrededor de un `yield return` en un iterator, así
            // que envolvemos cada ReadAsync por separado y propagamos la excepción adecuada.
            while (true)
            {
                var chunk = await ReadNextChunkAsync(channel, correlationId, ct).ConfigureAwait(false);
                if (chunk is null)
                {
                    // Stream completado normalmente (channel.Writer.Complete sin error).
                    yield break;
                }
                yield return chunk;
            }
        }
        finally
        {
            _streams.TryRemove(correlationId, out _);
        }
    }

    /// <summary>
    /// Lee el siguiente <see cref="LogChunk"/> del canal aplicando el watchdog
    /// <c>_streamChunkTimeout</c>. Devuelve <c>null</c> si el stream se completó normalmente.
    /// Lanza <see cref="TimeoutException"/> si el satélite no envió ningún chunk en el tiempo
    /// configurado y <see cref="OperationCanceledException"/> si el caller canceló.
    /// </summary>
    private async Task<LogChunk?> ReadNextChunkAsync(
        Channel<LogChunk> channel, string correlationId, CancellationToken ct)
    {
        using var watchdog = new CancellationTokenSource(_streamChunkTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, watchdog.Token);
        try
        {
            if (!await channel.Reader.WaitToReadAsync(linked.Token).ConfigureAwait(false))
            {
                // Channel completado sin error → fin natural del stream.
                return null;
            }
            return channel.Reader.TryRead(out var chunk) ? chunk : null;
        }
        catch (OperationCanceledException) when (watchdog.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Sin chunks del satélite en {_streamChunkTimeout.TotalSeconds}s (correlationId={correlationId}).");
        }
    }

    // -------------------------------------------------------------------------
    // Callbacks que SatelliteHub invoca cuando llega una respuesta del satélite.
    // -------------------------------------------------------------------------

    /// <summary>Resuelve la <see cref="Task"/> asociada a <paramref name="correlationId"/> con
    /// el objeto de respuesta. Para llamadas void (Stop/Remove) <paramref name="response"/>
    /// puede ser un objeto cualquiera (no se inspecciona).</summary>
    public void CompleteRequest(string correlationId, object response)
    {
        if (_pending.TryGetValue(correlationId, out var tcs))
        {
            tcs.TrySetResult(response);
        }
        else
        {
            _logger.LogWarning(
                "Respuesta de satélite con correlationId={CorrelationId} no tenía request pendiente.",
                correlationId);
        }
    }

    /// <summary>Propaga un fallo del satélite a la <see cref="Task"/> asociada al
    /// <paramref name="correlationId"/>.</summary>
    public void FailRequest(string correlationId, Exception error)
    {
        if (_pending.TryGetValue(correlationId, out var tcs))
        {
            tcs.TrySetException(error);
        }
    }

    /// <summary>Empuja un chunk de log al stream identificado por <paramref name="correlationId"/>.</summary>
    public void PushLogChunk(LogChunk chunk)
    {
        if (_streams.TryGetValue(chunk.CorrelationId, out var channel))
        {
            channel.Writer.TryWrite(chunk);
        }
    }

    /// <summary>Cierra el stream de logs (el satélite indicó EOF o error).</summary>
    public void CompleteStream(string correlationId, Exception? error = null)
    {
        if (_streams.TryGetValue(correlationId, out var channel))
        {
            channel.Writer.TryComplete(error);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers.
    // -------------------------------------------------------------------------

    private async Task<TResponse> SendAndAwaitAsync<TResponse>(
        string vmId, string correlationId, string method, object payload,
        TimeSpan timeout, CancellationToken ct)
        where TResponse : class
    {
        // Pre-check: si el satélite no está conectado, fallar rápido en lugar de esperar
        // al timeout. El registry se mantiene actualizado por SatelliteHub.OnConnected/Disconnected.
        var connectionId = _registry.GetConnectionId(vmId)
            ?? throw new SatelliteNotConnectedException(vmId);

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(correlationId, tcs))
        {
            throw new InvalidOperationException(
                $"Colisión inesperada de correlationId={correlationId}.");
        }

        try
        {
            // Apuntamos directamente al connectionId del satélite. Esto evita race-conditions
            // con el Groups (el satélite puede haberse unido al grupo pero la membresía aún
            // no haberse propagado en backplanes) y nos permite distinguir "no conectado" de
            // "conectado pero ocupado".
            await _hub.Clients.Client(connectionId)
                .SendAsync(method, payload, ct).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var timeoutTask = Task.Delay(timeout, timeoutCts.Token);
            var completed = await Task.WhenAny(tcs.Task, timeoutTask).ConfigureAwait(false);
            if (completed != tcs.Task)
            {
                // Distinguir cancelación del caller vs timeout real. Si el ct externo fue
                // cancelado, el Task.Delay también termina pero NO es un timeout: el caller
                // pidió aborto. Reportar TimeoutException en ese caso enmascaraba la causa
                // real en logs y en el errorMessage del Build/Deployment.
                if (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException(
                        $"Operación cancelada por el caller (vmId={vmId}, method={method}, correlationId={correlationId}).",
                        ct);
                }
                _logger.LogWarning(
                    "Timeout {Seconds}s esperando respuesta del satélite para vmId={VmId} method={Method} correlationId={CorrelationId}.",
                    timeout.TotalSeconds, vmId, method, correlationId);
                throw new TimeoutException(
                    $"Satélite {vmId} no respondió en {timeout.TotalSeconds}s a {method}.");
            }

            // Cancelar el delay para liberar recursos del timer.
            timeoutCts.Cancel();

            var raw = await tcs.Task.ConfigureAwait(false);
            if (raw is TResponse typed)
            {
                return typed;
            }
            // Para llamadas void (Stop/Remove) el caller pide TResponse=object — cualquier
            // valor (incluso null) se considera éxito.
            if (typeof(TResponse) == typeof(object))
            {
                return (TResponse)(raw ?? new object());
            }
            throw new InvalidOperationException(
                $"Respuesta del satélite con tipo inesperado: {raw?.GetType().FullName ?? "<null>"}, esperado {typeof(TResponse).FullName}.");
        }
        finally
        {
            _pending.TryRemove(correlationId, out _);
        }
    }

    private static string NewCorrelationId() => Guid.NewGuid().ToString("N");
}
