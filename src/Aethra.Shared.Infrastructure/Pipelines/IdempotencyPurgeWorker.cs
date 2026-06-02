using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aethra.Shared.Infrastructure.Pipelines;

/// <summary>
/// BackgroundService que purga entradas de <c>shared.idempotency_keys</c> con
/// <c>ExpiresAt &lt; now</c>. Sin esto la tabla crece sin tope (las keys se insertan en
/// cada comando idempotente pero nadie las borra). Corre cada 1h con un delay inicial
/// de 5min para no pegarle al boot.
/// </summary>
public sealed class IdempotencyPurgeWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<IdempotencyPurgeWorker> logger,
    TimeProvider clock) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "IdempotencyPurgeWorker arrancando — delay inicial {InitialDelay}, intervalo {Interval}",
            InitialDelay, Interval);

        try
        {
            await Task.Delay(InitialDelay, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPassAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
#pragma warning disable CA1031 // Lazo principal: capturar cualquier excepción para no matar al host.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "IdempotencyPurgeWorker falló en el loop principal");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunPassAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SharedDbContext>();

        var now = clock.GetUtcNow();
        var deleted = await db.IdempotencyKeys
            .Where(k => k.ExpiresAt < now)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        if (deleted > 0)
        {
            logger.LogInformation("IdempotencyPurgeWorker borró {Count} key(s) expiradas", deleted);
        }
        else
        {
            logger.LogDebug("IdempotencyPurgeWorker: 0 keys expiradas en este pase");
        }
    }
}
