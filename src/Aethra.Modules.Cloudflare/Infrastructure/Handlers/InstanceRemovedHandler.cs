using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Modules.Cloudflare.UseCases.DnsRecords.Commands;
using Aethra.Shared.Contracts.Projects;
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
}
