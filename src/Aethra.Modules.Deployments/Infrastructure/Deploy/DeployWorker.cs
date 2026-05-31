using Aethra.Modules.Deployments.UseCases.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Deployments.Infrastructure.Deploy;

/// <summary>
/// BackgroundService que consume <see cref="IDeployJobQueue"/> en orden FIFO.
/// Procesa jobs uno por uno (no paralelo) en F4 para mantener simplicidad. F5+ podría
/// permitir N workers en paralelo por VM target.
/// </summary>
public sealed class DeployWorker(
    IDeployJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<DeployWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DeployWorker arrancando");
        await foreach (var jobId in queue.ReadAllAsync(stoppingToken))
        {
            using var scope = scopeFactory.CreateScope();
            try
            {
                var orchestrator = scope.ServiceProvider.GetRequiredService<IDeployOrchestrator>();
                await orchestrator.RunAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error procesando DeployJob {Id}", jobId);
            }
        }
    }
}
