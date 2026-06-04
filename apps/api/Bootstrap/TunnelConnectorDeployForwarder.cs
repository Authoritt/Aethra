using Aethra.Shared.Contracts.Cloudflare;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aethra.Api.Bootstrap;

/// <summary>
/// F13.12 — suscriptor host del evento de deploy de connector cloudflared (disparado por la tool MCP).
/// Corre el deploy en BACKGROUND con scope propio (igual patrón que <see cref="NativeRedeployForwarder"/>).
/// </summary>
internal sealed class TunnelConnectorDeployForwarder(
    IServiceScopeFactory scopeFactory,
    ILogger<TunnelConnectorDeployForwarder> logger)
    : INotificationHandler<TunnelConnectorDeployRequestedIntegrationEvent>
{
    public Task Handle(TunnelConnectorDeployRequestedIntegrationEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("tunnel-connector deploy encolado ({Reason}, vm={Vm})", notification.Reason, notification.VmId ?? "auto");
        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var deployer = scope.ServiceProvider.GetRequiredService<CloudflareConnectorDeployer>();
            try
            {
                var r = await deployer.DeployAsync(notification.VmId, CancellationToken.None).ConfigureAwait(false);
                if (r.Success)
                {
                    logger.LogInformation("tunnel-connector OK en VM {Vm} ({Name})", r.VmId, r.ContainerName);
                }
                else
                {
                    logger.LogError("tunnel-connector falló: {Err}", r.Error);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "tunnel-connector excepción");
            }
        }, CancellationToken.None);
        return Task.CompletedTask;
    }
}
