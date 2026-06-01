using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Deployments.Infrastructure.Deployment;

/// <summary>
/// <see cref="BackgroundService"/> que consume el <see cref="IDeploymentJobQueue"/> en orden
/// FIFO. Procesa deployments uno por uno (no paralelo) en F9.3 para mantener simplicidad y
/// evitar contención sobre el (futuro) satélite — un deployment a la vez por nodo central.
///
/// Cada iteración crea su propio <see cref="IServiceScope"/> para resolver un
/// <see cref="IDeploymentOrchestrator"/> Scoped con su <c>DeploymentsDbContext</c> fresco.
/// </summary>
public sealed class DeploymentWorker(
    IDeploymentJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<DeploymentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DeploymentWorker arrancando");
        await foreach (var deploymentId in queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            using var scope = scopeFactory.CreateScope();
            try
            {
                var orchestrator = scope.ServiceProvider
                    .GetRequiredService<IDeploymentOrchestrator>();
                await orchestrator.RunAsync(deploymentId, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
#pragma warning disable CA1031 // Lazo principal: capturamos todo para que el worker no muera ante un deployment puntual.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "Error procesando Deployment {Id}", deploymentId);
            }
        }
    }
}
