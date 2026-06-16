using Aethra.Modules.Monitoring.Infrastructure;
using Aethra.Shared.Contracts.Projects;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Monitoring.Infrastructure.Handlers;

/// <summary>
/// Suscriptor cross-module del teardown de Instance: borra el/los monitor(es) cuyo
/// <c>InstanceId</c> es la instance eliminada, para que no quede un monitor huérfano chequeando
/// (y alertando sobre) un host que ya no existe. Idempotente: si no hay monitores, no-op.
/// </summary>
internal sealed class InstanceRemovedHandler(
    MonitoringDbContext db,
    ILogger<InstanceRemovedHandler> logger)
    : INotificationHandler<InstanceRemovedIntegrationEvent>
{
    public async Task Handle(InstanceRemovedIntegrationEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var monitors = await db.Monitors
            .Where(m => m.InstanceId == notification.InstanceId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (monitors.Count == 0)
        {
            return;
        }

        db.Monitors.RemoveRange(monitors);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "InstanceRemoved {Id}: {Count} monitor(es) eliminados", notification.InstanceId, monitors.Count);
    }
}
