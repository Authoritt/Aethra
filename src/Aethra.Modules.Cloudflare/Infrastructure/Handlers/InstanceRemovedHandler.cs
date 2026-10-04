using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Modules.Cloudflare.UseCases.DnsRecords.Commands;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Contracts.Proxy;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Cloudflare.Infrastructure.Handlers;

/// <summary>
/// Suscriptor cross-module del teardown de Instance: Cloudflare elimina el/los DNS record(s) del
/// hostname (custom + auto) para que no quede un CNAME huérfano apuntando al túnel (lo que dejaba
/// hosts que respondían 404/530). Reutiliza <see cref="DeleteDnsRecordCommand"/> (borra en Cloudflare
/// + copia local). Best-effort: si la API falla, loguea y sigue — no rompe el resto del teardown.
/// </summary>
internal sealed class InstanceRemovedHandler(
    CloudflareDbContext db,
    IMediator mediator,
    IRouteHostLookup routeHosts,
    IHostnameUsageLookup hostnameUsage,
    ILogger<InstanceRemovedHandler> logger)
    : INotificationHandler<InstanceRemovedIntegrationEvent>
{
    public async Task Handle(InstanceRemovedIntegrationEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var hostnames = new[] { notification.CustomDomain, notification.AutoHostname }
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // OT-009 (G2) — los hosts de override se borran SOLO si nadie más los usa: ni otra instancia
        // (CustomDomain/AutoHostname/override) ni una ruta del proxy de otro dueño (o sin dueño). Un
        // override que coincidiera con un host de producción no puede llevarse su DNS. Las rutas de
        // ESTA instancia no cuentan (Proxy puede no haberlas borrado aún: orden de handlers libre).
        foreach (var overrideHost in (notification.ServiceHostnames ?? [])
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (hostnames.Contains(overrideHost, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            if (await IsUsedByOthersAsync(overrideHost, notification.InstanceId, cancellationToken).ConfigureAwait(false))
            {
                logger.LogWarning(
                    "InstanceRemoved {Id}: DNS '{Host}' NO se borra: otra instancia o ruta aún lo usa",
                    notification.InstanceId, overrideHost);
                continue;
            }
            hostnames.Add(overrideHost);
        }
        if (hostnames.Count == 0)
        {
            return;
        }

        foreach (var host in hostnames)
        {
            var record = await db.DnsRecords.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Name == host, cancellationToken)
                .ConfigureAwait(false);
            if (record is null)
            {
                continue;
            }

            var result = await mediator
                .Send(new DeleteDnsRecordCommand(record.Id.ToString()), cancellationToken)
                .ConfigureAwait(false);
            if (result.IsSuccess)
            {
                logger.LogInformation("InstanceRemoved {Id}: DNS '{Host}' eliminado", notification.InstanceId, host);
            }
            else
            {
                logger.LogWarning(
                    "InstanceRemoved {Id}: no se pudo eliminar DNS '{Host}' ({Code}) — best-effort",
                    notification.InstanceId, host, result.Error.Code);
            }
        }
    }

    private async Task<bool> IsUsedByOthersAsync(string host, string instanceId, CancellationToken ct)
    {
        var instances = await hostnameUsage.FindInstancesUsingHostnameAsync(host, instanceId, ct).ConfigureAwait(false);
        if (instances.Count > 0)
        {
            return true;
        }
        var routes = await routeHosts.FindByHostnameAsync(host, ct).ConfigureAwait(false);
        return routes.Any(r => !string.Equals(r.OperationalOwnerId, instanceId, StringComparison.Ordinal));
    }
}
