using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aethra.Modules.Deployments.Infrastructure;

/// <summary>
/// BackgroundService que purga Builds/Deployments (y sus logs) más viejos que
/// <see cref="DeploymentsRetentionOptions.RetentionDays"/>. Los *Log crecen línea por línea por build/deploy
/// y nunca se purgaban → fuga de disco. Borra primero los logs (por Timestamp) y luego los padres (por
/// CreatedAt); el cascade del FK cubre cualquier log remanente. Espejo de los demás retention workers.
/// </summary>
public sealed class DeploymentsRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<DeploymentsRetentionOptions> options,
    ILogger<DeploymentsRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (opts.RetentionDays <= 0)
        {
            logger.LogInformation("Retención de Builds/Deployments desactivada (RetentionDays={Days}).", opts.RetentionDays);
            return;
        }

        var sweep = TimeSpan.FromHours(opts.SweepIntervalHours <= 0 ? 12 : opts.SweepIntervalHours);
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(6), stoppingToken).ConfigureAwait(false);
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
                logger.LogWarning(ex, "Barrido de retención de Builds/Deployments falló; reintenta en {Sweep}.", sweep);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task PruneAsync(int retentionDays, CancellationToken ct)
    {
        var cutoff = clock.UtcNow - TimeSpan.FromDays(retentionDays);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DeploymentsDbContext>();

        // Logs primero (por timestamp), luego padres (por CreatedAt); el cascade cubre remanentes.
        var buildLogs = await db.BuildLogs.Where(l => l.Timestamp < cutoff).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        var builds = await db.Builds.Where(b => b.CreatedAt < cutoff).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        var deployLogs = await db.DeploymentLogs.Where(l => l.Timestamp < cutoff).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        var deploys = await db.Deployments.Where(d => d.CreatedAt < cutoff).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        if (builds > 0 || deploys > 0 || buildLogs > 0 || deployLogs > 0)
        {
            logger.LogInformation(
                "Retención de Deployments: borrados {Builds} builds (+{BuildLogs} logs) y {Deploys} deployments (+{DeployLogs} logs) anteriores a {Cutoff:o} ({Days}d).",
                builds, buildLogs, deploys, deployLogs, cutoff, retentionDays);
        }
    }
}
