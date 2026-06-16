using Aethra.Modules.Proxy.Infrastructure.Yarp;
using Aethra.Shared.Contracts.Projects;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Proxy.Infrastructure.Handlers;

/// <summary>
/// Suscriptor cross-module: cuando Projects borra una <c>Instance</c>, el Proxy elimina TODAS sus
/// <c>Route</c>s YARP para que deje de aceptar tráfico. Una app multi-path (p. ej. <c>/</c>, <c>/api</c>,
/// <c>/hubs</c>) tiene varias rutas para el mismo hostname; antes este handler quitaba sólo UNA
/// (FirstOrDefault) y dejaba huérfanas las demás. Ahora matchea por owner (<c>OperationalOwnerId ==
/// instanceId</c>, cubre todas las rutas de la instance) y por hostname (custom + auto, cubre rutas
/// sin owner). Idempotente: si no hay rutas, no-op.
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

        var hostnames = new[] { notification.CustomDomain, notification.AutoHostname }
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // La tabla Routes es chica (decenas de filas); traerla y filtrar en memoria evita traducir
        // el value object Hostname + un OR complejo a SQL.
        var allRoutes = await db.Routes.ToListAsync(cancellationToken).ConfigureAwait(false);
        var toRemove = allRoutes
            .Where(r =>
                string.Equals(r.OperationalOwnerId, notification.InstanceId, StringComparison.Ordinal)
                || hostnames.Contains(r.Hostname.Value))
            .ToList();

        if (toRemove.Count == 0)
        {
            logger.LogInformation(
                "InstanceRemoved {Id}: no había Routes (owner/hostname) — no-op", notification.InstanceId);
            return;
        }

        foreach (var route in toRemove)
        {
            route.MarkRemoved();
            db.Routes.Remove(route);
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        config.Reload();

        logger.LogInformation(
            "InstanceRemoved {Id}: {Count} Route(s) eliminadas ({Hosts})",
            notification.InstanceId, toRemove.Count,
            string.Join(", ", toRemove.Select(r => r.Hostname.Value).Distinct()));
    }
}
