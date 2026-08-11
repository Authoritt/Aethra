using Aethra.Shared.Contracts.Deployments;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aethra.Api.Bootstrap;

/// <summary>
/// F13.1 — suscriptor host del evento de redeploy nativo (disparado por el webhook de push para
/// Templates multi-servicio). Ejecuta el deploy en BACKGROUND con un scope nuevo para no bloquear
/// la respuesta del webhook (GitHub espera &lt;10s; un build-from-git puede tardar minutos).
/// </summary>
internal sealed class NativeRedeployForwarder(
    IServiceScopeFactory scopeFactory,
    ILogger<NativeRedeployForwarder> logger)
    : INotificationHandler<NativeRedeployRequestedIntegrationEvent>
{
    public Task Handle(NativeRedeployRequestedIntegrationEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("native-redeploy encolado para instance {Inst} ({Reason})",
            notification.InstanceId, notification.Reason);

        // Fire-and-forget: scope propio + CancellationToken.None (no atado al request del webhook).
        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<NativeDeployRunner>();
            try
            {
                var r = await runner.DeployAsync(
                        notification.InstanceId,
                        hostnameOverride: null,
                        CancellationToken.None,
                        notification.ServiceName)
                    .ConfigureAwait(false);
                if (r.Success)
                {
                    if (r.Warnings.Count > 0)
                    {
                        logger.LogWarning(
                            "native-redeploy {Inst} OK con {WarningCount} aviso(s) (healthy={H}): {Warnings}",
                            notification.InstanceId,
                            r.Warnings.Count,
                            r.Healthy,
                            r.Warnings);
                    }
                    else
                    {
                        logger.LogInformation(
                            "native-redeploy {Inst} OK sin avisos (healthy={H})",
                            notification.InstanceId,
                            r.Healthy);
                    }
                }
                else
                {
                    logger.LogError("native-redeploy {Inst} falló: {Err}", notification.InstanceId, r.Error);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "native-redeploy {Inst} excepción", notification.InstanceId);
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }
}
