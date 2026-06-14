using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aethra.Modules.Monitoring.Infrastructure.Worker;

/// <summary>
/// BackgroundService que purga <c>MonitorCheck</c> más viejos que <see cref="MonitoringRetentionOptions.RetentionDays"/>,
/// evitando el crecimiento ilimitado del disco (el MonitorWorker escribe un check por monitor por intervalo).
/// Espejo de MetricsRetentionWorker. Usa ExecuteDeleteAsync (un DELETE, sin materializar).
/// </summary>
public sealed class MonitorCheckRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<MonitoringRetentionOptions> options,
    ILogger<MonitorCheckRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (opts.RetentionDays <= 0)
        {
            logger.LogInformation("Retención de MonitorChecks desactivada (RetentionDays={Days}).", opts.RetentionDays);
            return;
        }

        var sweep = TimeSpan.FromHours(opts.SweepIntervalHours <= 0 ? 6 : opts.SweepIntervalHours);
        // Delay inicial (distinto del de métricas) para no barrer ambas tablas a la vez en el arranque.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(sweep);
        do
        {
            try
            {
                await PruneAsync(opts.RetentionDays, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Barrido de retención de MonitorChecks falló; reintenta en {Sweep}.", sweep);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task PruneAsync(int retentionDays, CancellationToken ct)
    {
        var cutoff = clock.UtcNow - TimeSpan.FromDays(retentionDays);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MonitoringDbContext>();

        var deleted = await db.MonitorChecks.Where(c => c.Timestamp < cutoff)
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);

        if (deleted > 0)
        {
            logger.LogInformation(
                "Retención de MonitorChecks: borradas {N} filas anteriores a {Cutoff:o} (retención {Days}d).",
                deleted, cutoff, retentionDays);
        }
    }
}
