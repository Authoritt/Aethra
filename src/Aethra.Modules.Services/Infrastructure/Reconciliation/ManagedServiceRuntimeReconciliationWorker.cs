using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Services.Infrastructure.Reconciliation;

internal sealed class ManagedServiceRuntimeReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ManagedServiceRuntimeReconciliationWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ReconcileOnceAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await ReconcileOnceAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ReconcileOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var reconciler = scope.ServiceProvider.GetRequiredService<ManagedServiceRuntimeReconciler>();
            await reconciler.ReconcileRuntimeServicesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Fallo el reconciliador de servicios gestionados");
        }
    }
}
