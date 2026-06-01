using Aethra.Modules.Proxy.Infrastructure.Yarp;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Kernel.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Proxy.Infrastructure.Handlers;

/// <summary>
/// Suscriptor cross-module: cuando Projects borra una <c>Instance</c>, el Proxy elimina la
/// <c>Route</c> YARP asociada para que YARP deje de aceptar tráfico al hostname. Es idempotente:
/// si no existe la Route (lifecycle inverso, p. ej. Instance creada en modo headless), no falla.
/// </summary>
internal sealed class InstanceRemovedHandler(
    ProxyDbContext db,
    IProxyConfigService config,
    ILogger<InstanceRemovedHandler> logger)
    : INotificationHandler<InstanceRemovedIntegrationEvent>
{
    public async Task Handle(InstanceRemovedIntegrationEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var hostnameValue = notification.CustomDomain ?? notification.AutoHostname;
        if (string.IsNullOrWhiteSpace(hostnameValue))
        {
            logger.LogInformation(
                "InstanceRemoved {Id}: sin hostname asociado — nada que limpiar",
                notification.InstanceId);
            return;
        }

        var hostnameResult = Hostname.Create(hostnameValue);
        if (hostnameResult.IsFailure)
        {
            logger.LogWarning(
                "InstanceRemoved {Id}: hostname inválido '{Hostname}' ({Code}) — skip",
                notification.InstanceId, hostnameValue, hostnameResult.Error.Code);
            return;
        }
        var hostname = hostnameResult.Value;

        var existing = await db.Routes
            .FirstOrDefaultAsync(r => r.Hostname == hostname, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            logger.LogInformation(
                "InstanceRemoved {Id}: no había Route para '{Hostname}' — no-op",
                notification.InstanceId, hostname.Value);
            return;
        }

        existing.MarkRemoved();
        db.Routes.Remove(existing);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        config.Reload();

        logger.LogInformation(
            "InstanceRemoved {Id}: Route {RouteId} eliminada ({Hostname})",
            notification.InstanceId, existing.Id, hostname.Value);
    }
}
