using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aethra.Modules.Services.Infrastructure.Scheduling;

/// <summary>
/// BackgroundService que purga <c>ScheduledJobRun</c> más viejas que
/// <see cref="ScheduledJobRunRetentionOptions.RunRetentionDays"/> (cada corrida guarda stdout/stderr y
/// nunca se purgaban → fuga de disco). Espejo de los demás retention workers; ExecuteDeleteAsync.
/// </summary>
public sealed class ScheduledJobRunRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<ScheduledJobRunRetentionOptions> options,
    ILogger<ScheduledJobRunRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (opts.RunRetentionDays <= 0)
        {
            logger.LogInformation("Retención de ScheduledJobRuns desactivada (RunRetentionDays={Days}).", opts.RunRetentionDays);
            return;
        }

        var sweep = TimeSpan.FromHours(opts.SweepIntervalHours <= 0 ? 12 : opts.SweepIntervalHours);
        // Delay inicial distinto de los otros workers para escalonar el arranque.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(4), stoppingToken).ConfigureAwait(false);
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
                await PruneAsync(opts.RunRetentionDays, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Barrido de retención de ScheduledJobRuns falló; reintenta en {Sweep}.", sweep);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task PruneAsync(int retentionDays, CancellationToken ct)
    {
        var cutoff = clock.UtcNow - TimeSpan.FromDays(retentionDays);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServicesDbContext>();

        var deleted = await db.ScheduledJobRuns.Where(r => r.StartedAt < cutoff)
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);

        if (deleted > 0)
        {
            logger.LogInformation(
                "Retención de ScheduledJobRuns: borradas {N} corridas anteriores a {Cutoff:o} (retención {Days}d).",
                deleted, cutoff, retentionDays);
        }
    }
}
