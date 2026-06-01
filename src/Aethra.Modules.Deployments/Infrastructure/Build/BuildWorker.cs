using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Deployments.Infrastructure.Build;

/// <summary>
/// <see cref="BackgroundService"/> que consume el <see cref="IBuildJobQueue"/> en orden
/// FIFO. Procesa builds uno por uno (no paralelo) en F9.3 para mantener simplicidad y
/// evitar contención sobre el (futuro) satélite — un build a la vez por nodo central.
///
/// Cada iteración crea su propio <see cref="IServiceScope"/> para resolver un
/// <see cref="IBuildOrchestrator"/> Scoped con su <c>DeploymentsDbContext</c> fresco.
/// </summary>
public sealed class BuildWorker(
    IBuildJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<BuildWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("BuildWorker arrancando");
        await foreach (var buildId in queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            using var scope = scopeFactory.CreateScope();
            try
            {
                var orchestrator = scope.ServiceProvider.GetRequiredService<IBuildOrchestrator>();
                await orchestrator.RunAsync(buildId, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error procesando Build {Id}", buildId);
            }
        }
    }
}
