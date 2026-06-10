using System.Diagnostics;
using Aethra.Satellite.Buffer;
using Aethra.Satellite.Probes;
using Aethra.Shared.Contracts.Vms;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aethra.Satellite.Workers;

/// <summary>
/// Mantiene la conexión SignalR al central, hace handshake al conectar y dispara muestras
/// periódicas de métricas. Reconexión con backoff [0,2,10,30,60]s + 60s indefinido.
///
/// Patrón "replication" estilo Netdata: TODA muestra se persiste primero en el
/// <see cref="ISnapshotBuffer"/> local. Si hay conexión, drenamos el buffer en orden
/// cronológico y solo borramos las entradas confirmadas por el central. Si el central
/// se cae, las muestras se acumulan localmente (con ring buffer de 24h via prune) y se
/// drenan al reconectar — no se pierden métricas durante outages.
/// </summary>
public sealed class SatelliteConnectionWorker(
    IOptions<SatelliteOptions> options,
    IMetricsProbe probe,
    ISnapshotBuffer buffer,
    SatelliteCommandHandler commandHandler,
    ILogger<SatelliteConnectionWorker> logger) : BackgroundService
{
    private const string ReportMetricsMethod = "ReportMetrics";
    private const int DrainBatchSize = 50;
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromHours(24);

    private HubConnection? _connection;
    private DateTimeOffset _lastPruneAt = DateTimeOffset.MinValue;
    private int _enqueueCounter;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.Token))
        {
            logger.LogError("AETHRA_SATELLITE_TOKEN no configurado. El satélite no puede arrancar.");
            return;
        }

        _connection = new HubConnectionBuilder()
            .WithUrl($"{opts.CentralUrl.TrimEnd('/')}/hubs/satellite",
                http => http.AccessTokenProvider = () => Task.FromResult<string?>(opts.Token))
            .WithAutomaticReconnect(new SatelliteReconnectPolicy())
            .Build();

        // Registramos los handlers de comandos central→satélite (F4) ANTES de arrancar.
        // Los .On<>() persisten a través de reconnects, así que basta una sola vez.
        ConfigureCommandHandlers();

        _connection.Reconnected += async (id) =>
        {
            logger.LogInformation("Reconectado al central, id={Id}. Re-handshake.", id);
            await SendHandshakeAsync(stoppingToken);
        };

        _connection.Closed += (ex) =>
        {
            logger.LogWarning(ex, "Conexión cerrada. Auto-reconnect intentará seguir.");
            return Task.CompletedTask;
        };

        await StartWithRetryAsync(stoppingToken);
        await SendHandshakeAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(opts.MetricsIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                // 1) Muestreamos SIEMPRE y persistimos en el buffer local. Esto garantiza
                //    que ni una sola muestra se pierda aunque el central esté caído.
                var snapshot = await probe.SnapshotAsync(stoppingToken);
                await buffer.EnqueueAsync(snapshot, stoppingToken);

                if (_connection.State != HubConnectionState.Connected)
                {
                    // Log con muestreo: 1 de cada 10 enqueues para no saturar.
                    var count = Interlocked.Increment(ref _enqueueCounter);
                    if (count % 10 == 1)
                    {
                        logger.LogInformation(
                            "Buffereada métrica (conexión = {State}); buffer ahora tiene ~{N} pendientes",
                            _connection.State, count);
                    }
                    continue;
                }

                // 2) Conectados: drenamos en orden cronológico (FIFO).
                _enqueueCounter = 0;
                await DrainAndSendAsync(stoppingToken);

                // 3) Prune oportunista (cada PruneInterval) para mantener el ring buffer de 24h.
                await MaybePruneAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error en ciclo de métricas");
            }
        }
    }

    /// <summary>
    /// Lee un batch del buffer y lo envía en orden. Solo marca como enviados los que
    /// confirmaron entrega. Si un envío falla, se detiene el drain para preservar orden
    /// cronológico (el snapshot fallido queda en el buffer y se reintentará en el próximo tick).
    /// </summary>
    private async Task DrainAndSendAsync(CancellationToken ct)
    {
        if (_connection is null)
        {
            return;
        }

        var batch = await buffer.DrainBatchAsync(DrainBatchSize, ct);
        if (batch.Count == 0)
        {
            return;
        }

        var sentIds = new List<long>(batch.Count);
        foreach (var entry in batch)
        {
            if (_connection.State != HubConnectionState.Connected)
            {
                // La conexión se cayó a mitad del drain; preservamos los pendientes.
                break;
            }

            using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            sendCts.CancelAfter(SendTimeout);
            try
            {
                await _connection.InvokeAsync(ReportMetricsMethod, entry.Snapshot, sendCts.Token);
                sentIds.Add(entry.Id);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Detenemos el drain: dejamos este snapshot y los siguientes para el próximo ciclo,
                // así no rompemos el orden FIFO.
                logger.LogWarning(ex, "Envío de snapshot id={Id} falló; se mantendrá en buffer", entry.Id);
                break;
            }
        }

        if (sentIds.Count > 0)
        {
            await buffer.MarkSentAsync(sentIds, ct);
            logger.LogInformation("Drenando buffer: enviadas {N} muestras pendientes", sentIds.Count);
        }
    }

    /// <summary>Ejecuta <see cref="ISnapshotBuffer.PruneOlderThanAsync"/> cada <see cref="PruneInterval"/>.</summary>
    private async Task MaybePruneAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastPruneAt < PruneInterval)
        {
            return;
        }
        _lastPruneAt = now;
        await buffer.PruneOlderThanAsync(now - RetentionWindow, ct);
    }

    private async Task SendHandshakeAsync(CancellationToken ct)
    {
        if (_connection is null)
        {
            return;
        }
        try
        {
            var info = await probe.HandshakeAsync(ct);
            var runtime = options.Value.ContainerRuntime;
            info = info with
            {
                ContainerRuntime = runtime,
                ContainerRuntimeVersion = await DetectRuntimeVersion(runtime, ct),
            };
            await _connection.InvokeAsync("Handshake", info, ct);
            logger.LogInformation(
                "Handshake enviado: host={Host} kernel={Kernel} cpu={Cpu} cores={Cores} runtime={Runtime}",
                info.Hostname, info.KernelVersion, info.CpuModel, info.CpuCores, info.ContainerRuntime);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Handshake falló");
        }
    }

    private static async Task<string?> DetectRuntimeVersion(string runtime, CancellationToken ct)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = runtime,
                ArgumentList = { "--version" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null)
            {
                return null;
            }
            var output = await process.StandardOutput.ReadLineAsync(ct);
            await process.WaitForExitAsync(ct);
            return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Engancha en el <see cref="HubConnection"/> los handlers de comandos remotos
    /// (BuildImage, RunContainer, StopContainer, RemoveContainer, StreamLogs, ListContainers).
    /// El registro se hace una sola vez tras crear la conexión — los <c>.On&lt;&gt;()</c>
    /// persisten a través de los auto-reconnects de SignalR.
    /// </summary>
    private void ConfigureCommandHandlers()
    {
        if (_connection is null)
        {
            return;
        }
        commandHandler.Register(_connection);
    }

    private async Task StartWithRetryAsync(CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested && _connection!.State != HubConnectionState.Connected)
        {
            try
            {
                await _connection.StartAsync(ct);
                logger.LogInformation("Conectado al central");
                return;
            }
            catch (Exception ex)
            {
                attempt++;
                var delay = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Min(attempt, 6))));
                logger.LogWarning(ex, "No se pudo conectar (intento {Attempt}); reintento en {Delay}", attempt, delay);
                try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { return; }
            }
        }
    }
}

internal sealed class SatelliteReconnectPolicy : IRetryPolicy
{
    private static readonly TimeSpan?[] Delays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
    ];

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
        => retryContext.PreviousRetryCount < Delays.Length
            ? Delays[retryContext.PreviousRetryCount]
            : TimeSpan.FromSeconds(60);
}
