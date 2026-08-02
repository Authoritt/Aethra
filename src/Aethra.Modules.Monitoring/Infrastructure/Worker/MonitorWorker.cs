using System.Globalization;
using Aethra.Modules.Monitoring.Domain;
using Aethra.Modules.Monitoring.Domain.Events;
using Aethra.Modules.Monitoring.Infrastructure.Probes;
using Aethra.Shared.Contracts.Monitoring;
using Aethra.Shared.Infrastructure.Outbox;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Monitoring.Infrastructure.Worker;

/// <summary>
/// BackgroundService que ejecuta probes HTTP contra los monitores activos.
///
/// <para>Diseño:</para>
/// <list type="bullet">
///   <item>Tick configurable (Monitoring__TickSeconds, default 10s) — granularidad menor que el menor intervalo permitido (30s).</item>
///   <item>Por cada tick selecciona monitores <c>IsEnabled = true</c> cuyo <c>LastCheckedAt</c>
///         indique que ya toca, ordenados por <c>LastCheckedAt ASC NULLS FIRST</c> para no
///         starve a los que llevan más tiempo sin probarse.</item>
///   <item>Ejecuta probes con <see cref="Parallel.ForEachAsync"/> y un <c>MaxDegreeOfParallelism</c>
///         acotado para no saturar la red ni el HttpClient pool.</item>
///   <item>Cada probe + persistencia se hace en su propio scope (DbContext fresco) para evitar
///         contention sobre el mismo tracker — esto también desacopla el commit por monitor.</item>
///   <item>El evento <see cref="MonitorStatusChangedEvent"/> se traduce a integration event en el
///         outbox dentro de la misma transacción, garantizando at-least-once al SignalR forwarder.</item>
/// </list>
/// </summary>
public sealed class MonitorWorker(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    Microsoft.Extensions.Options.IOptions<MonitorWorkerOptions> opciones,
    ILogger<MonitorWorker> logger) : BackgroundService
{
    private const int MaxParallelism = 8;
    private const int FailureWarningThreshold = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tick = TickResuelto.Desde(opciones.Value.TickSeconds);
        if (tick.Recortado)
        {
            // El operador pidió algo que el sistema no puede cumplir. Se ajusta, pero se dice:
            // un recorte callado le deja creyendo que configuró algo que no está pasando.
            logger.LogWarning(
                "MonitorWorker: Monitoring__TickSeconds={Pedido} queda fuera de [{Min}, {Max}] y se usa "
                + "{Efectivo}s. El techo NO es arbitrario: es el intervalo minimo que un monitor puede "
                + "pedir, y con un tick mas largo ese monitor no puede sondearse a su ritmo.",
                tick.Pedido.ToString("0.##", CultureInfo.InvariantCulture),
                MonitorWorkerOptions.TickMinimo.ToString("0.##", CultureInfo.InvariantCulture),
                MonitorWorkerOptions.TickMaximo.ToString("0.##", CultureInfo.InvariantCulture),
                tick.Efectivo.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture));
        }

        logger.LogInformation("MonitorWorker arrancando con tick {Tick}s y paralelismo {Par}",
            tick.Efectivo.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture),
            MaxParallelism.ToString(CultureInfo.InvariantCulture));

        // Pequeño delay inicial para que la BD y migraciones estén listas.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(tick.Efectivo);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await RunTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
#pragma warning disable CA1031 // Lazo principal: no queremos que un error mate al worker.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "MonitorWorker falló en un tick");
            }
        }
    }

    private async Task RunTickAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        List<MonitorId> dueIds;

        // Scope corto solo para listar los IDs due — luego cada uno corre en su propio scope.
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MonitoringDbContext>();
            // Traemos todos los enabled ordenados por (LastCheckedAt NULLS FIRST, LastCheckedAt ASC)
            // y filtramos por intervalo en memoria. Es barato: cardinalidad de monitores activos
            // en un single-user / homelab es pequeña (decenas), no centenares de miles.
            var candidates = await db.Monitors
                .AsNoTracking()
                .Where(m => m.IsEnabled)
                .OrderBy(m => m.LastCheckedAt == null ? 0 : 1)
                .ThenBy(m => m.LastCheckedAt)
                .Select(m => new MonitorDueRow(m.Id, m.LastCheckedAt, m.IntervalSec))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            dueIds = candidates
                .Where(c => c.LastCheckedAt is null
                    || (now - c.LastCheckedAt.Value).TotalSeconds >= c.IntervalSec)
                .Select(c => c.Id)
                .ToList();
        }

        if (dueIds.Count == 0)
        {
            return;
        }

        logger.LogDebug("Tick monitors: {Count} due", dueIds.Count);

        await Parallel.ForEachAsync(
            dueIds,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxParallelism,
                CancellationToken = ct,
            },
            ProbeAndPersistAsync).ConfigureAwait(false);
    }

    private sealed record MonitorDueRow(MonitorId Id, DateTimeOffset? LastCheckedAt, int IntervalSec);

    private async ValueTask ProbeAndPersistAsync(MonitorId monitorId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MonitoringDbContext>();
        var probe = sp.GetRequiredService<IMonitorProbe>();
        var outbox = sp.GetRequiredService<IOutboxWriter<MonitoringDbContext>>();

        var monitor = await db.Monitors.FirstOrDefaultAsync(m => m.Id == monitorId, ct).ConfigureAwait(false);
        if (monitor is null)
        {
            return;
        }
        if (!monitor.IsEnabled)
        {
            return;
        }

        MonitorProbeResult probeResult;
        try
        {
            probeResult = await probe.ProbeAsync(monitor, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Probe es código de red: fallo inesperado → tratamos como Down y seguimos.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "Probe lanzó excepción para monitor {Slug}", monitor.Slug);
            probeResult = new MonitorProbeResult(
                MonitorStatus.Down, null, null, ex.Message, null);
        }

        var now = clock.UtcNow;
        var check = MonitorCheck.Create(
            monitor.Id,
            now,
            probeResult.Status,
            probeResult.HttpStatusCode,
            probeResult.LatencyMs,
            probeResult.ErrorMessage,
            probeResult.ResponseSnippet);

        db.MonitorChecks.Add(check);
        var previousStatus = monitor.LastStatus;
        monitor.RecordCheck(check);

        // Si el dominio decidió emitir un MonitorStatusChangedEvent, lo encolamos también
        // como integration event para que el SignalR forwarder y otros módulos lo reciban.
        if (monitor.DomainEvents.Count > 0)
        {
            foreach (var ev in monitor.DomainEvents)
            {
                if (ev is MonitorStatusChangedEvent statusChange)
                {
                    await outbox.EnqueueAsync(
                        new MonitorStatusChangedIntegrationEvent(
                            MonitorId: statusChange.MonitorId.ToString(),
                            From: statusChange.From.ToString(),
                            To: statusChange.To.ToString(),
                            CheckId: statusChange.CheckId.ToString(),
                            HttpStatusCode: probeResult.HttpStatusCode,
                            LatencyMs: probeResult.LatencyMs,
                            Timestamp: now),
                        ct).ConfigureAwait(false);
                }
            }
            monitor.ClearDomainEvents();
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (previousStatus != probeResult.Status)
        {
            logger.LogInformation(
                "Monitor {Slug}: {From} → {To} (HTTP {Code}, {Latency}ms)",
                monitor.Slug,
                previousStatus,
                probeResult.Status,
                probeResult.HttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "n/a",
                probeResult.LatencyMs?.ToString(CultureInfo.InvariantCulture) ?? "n/a");
        }
        else
        {
            logger.LogDebug(
                "Monitor {Slug}: {Status} (HTTP {Code}, {Latency}ms)",
                monitor.Slug,
                probeResult.Status,
                probeResult.HttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "n/a",
                probeResult.LatencyMs?.ToString(CultureInfo.InvariantCulture) ?? "n/a");
        }

        if (monitor.ConsecutiveFailures >= FailureWarningThreshold
            && (probeResult.Status == MonitorStatus.Down || probeResult.Status == MonitorStatus.Degraded))
        {
            logger.LogWarning(
                "Monitor {Slug} acumula {N} fallos consecutivos (último: {Status} {Code})",
                monitor.Slug,
                monitor.ConsecutiveFailures.ToString(CultureInfo.InvariantCulture),
                probeResult.Status,
                probeResult.HttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "n/a");
        }
    }
}
