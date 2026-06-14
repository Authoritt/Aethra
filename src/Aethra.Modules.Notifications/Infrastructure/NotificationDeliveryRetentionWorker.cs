using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aethra.Modules.Notifications.Infrastructure;

/// <summary>
/// BackgroundService que purga <c>NotificationDelivery</c> más viejos que
/// <see cref="NotificationsRetentionOptions.RetentionDays"/> (una fila por envío, nunca se purgaba →
/// fuga de disco). Espejo de los demás retention workers; ExecuteDeleteAsync por CreatedAt.
/// </summary>
public sealed class NotificationDeliveryRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<NotificationsRetentionOptions> options,
    ILogger<NotificationDeliveryRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (opts.RetentionDays <= 0)
        {
            logger.LogInformation("Retención de NotificationDeliveries desactivada (RetentionDays={Days}).", opts.RetentionDays);
            return;
        }

        var sweep = TimeSpan.FromHours(opts.SweepIntervalHours <= 0 ? 12 : opts.SweepIntervalHours);
        // Delay inicial escalonado respecto a los otros retention workers.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false);
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
                logger.LogWarning(ex, "Barrido de retención de NotificationDeliveries falló; reintenta en {Sweep}.", sweep);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task PruneAsync(int retentionDays, CancellationToken ct)
    {
        var cutoff = clock.UtcNow - TimeSpan.FromDays(retentionDays);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var deleted = await db.NotificationDeliveries.Where(d => d.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);

        if (deleted > 0)
        {
            logger.LogInformation(
                "Retención de NotificationDeliveries: borradas {N} filas anteriores a {Cutoff:o} (retención {Days}d).",
                deleted, cutoff, retentionDays);
        }
    }
}
