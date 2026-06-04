using Aethra.Shared.Contracts.Projects;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aethra.Api.Bootstrap;

/// <summary>
/// F13.8 — suscriptor host de los eventos de cambio de dominio de una Instance. Cuando el operador
/// "personaliza la URL" (set/cambia/quita el custom domain), reconcilia el routing NATIVO de la
/// Instance hacia su hostname deseado actual (<c>CustomDomain ?? AutoHostname</c>): crea las rutas
/// del host nuevo, borra las de la URL anterior, refresca CNAME + monitor — dejando todo limpio.
///
/// Ambos eventos disparan la MISMA reconciliación (idempotente): recalcula el host deseado desde el
/// estado actual de la Instance, así que no importa el orden Removed/Requested del batch del outbox.
/// Corre en background con scope propio (no bloquea el commit que emitió el evento).
/// </summary>
internal sealed class InstanceCustomDomainReconciler(
    IServiceScopeFactory scopeFactory,
    ILogger<InstanceCustomDomainReconciler> logger)
    : INotificationHandler<CustomDomainRequestedIntegrationEvent>,
      INotificationHandler<CustomDomainRemovedIntegrationEvent>
{
    public Task Handle(CustomDomainRequestedIntegrationEvent notification, CancellationToken cancellationToken)
        => ReconcileInBackground(notification.InstanceId, "custom-domain-set");

    public Task Handle(CustomDomainRemovedIntegrationEvent notification, CancellationToken cancellationToken)
        => ReconcileInBackground(notification.InstanceId, "custom-domain-removed");

    private Task ReconcileInBackground(string instanceId, string reason)
    {
        logger.LogInformation("reconcile-routing encolado para instance {Inst} ({Reason})", instanceId, reason);
        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<NativeDeployRunner>();
            try
            {
                await runner.ReconcileRoutingForInstanceAsync(instanceId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "reconcile-routing {Inst} excepción", instanceId);
            }
        }, CancellationToken.None);
        return Task.CompletedTask;
    }
}
