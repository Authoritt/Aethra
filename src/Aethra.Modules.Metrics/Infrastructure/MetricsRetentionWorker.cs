using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aethra.Modules.Metrics.Infrastructure;

/// <summary>
/// BackgroundService que purga métricas crudas (VmMetrics + ContainerSnapshots) más viejas que
/// <see cref="MetricsRetentionOptions.RetentionDays"/>, evitando el crecimiento ilimitado del disco
/// (el satélite reporta cada pocos segundos). Usa ExecuteDeleteAsync (un solo DELETE, sin materializar).
/// </summary>
public sealed class MetricsRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<MetricsRetentionOptions> options,
    ILogger<MetricsRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (opts.RetentionDays <= 0)
        {
            logger.LogInformation("Retención de métricas desactivada (RetentionDays={Days}).", opts.RetentionDays);
            return;
        }

        var sweep = TimeSpan.FromHours(opts.SweepIntervalHours <= 0 ? 6 : opts.SweepIntervalHours);
        // Pequeño delay inicial para no competir con el arranque (migraciones, seed).
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken).ConfigureAwait(false);
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
                logger.LogWarning(ex, "Barrido de retención de métricas falló; reintenta en {Sweep}.", sweep);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task PruneAsync(int retentionDays, CancellationToken ct)
    {
        var cutoff = clock.UtcNow - TimeSpan.FromDays(retentionDays);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetricsDbContext>();

        var vmDeleted = await db.VmMetrics.Where(m => m.Timestamp < cutoff)
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        var csDeleted = await db.ContainerSnapshots.Where(m => m.Timestamp < cutoff)
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);

        if (vmDeleted > 0 || csDeleted > 0)
        {
            logger.LogInformation(
                "Retención de métricas: borradas {Vm} VmMetrics + {Cs} ContainerSnapshots anteriores a {Cutoff:o} (retención {Days}d).",
                vmDeleted, csDeleted, cutoff, retentionDays);
        }
    }
}
