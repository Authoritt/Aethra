using Aethra.Modules.Metrics.Domain;
using Aethra.Modules.Metrics.Infrastructure;
using Aethra.Shared.Contracts.Vms;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Metrics.UseCases.Handlers;

/// <summary>
/// Suscriptor cross-module: cuando un satélite reporta métricas (evento publicado por el hub
/// de VMs), persistimos el snapshot en la tabla time-series.
/// </summary>
internal sealed class VmMetricsReportedHandler(MetricsDbContext db, ILogger<VmMetricsReportedHandler> logger)
    : INotificationHandler<VmMetricsReportedEvent>
{
    public async Task Handle(VmMetricsReportedEvent notification, CancellationToken cancellationToken)
    {
        db.VmMetrics.Add(VmMetricRecord.FromSnapshot(notification.VmId, notification.Snapshot));
        await db.SaveChangesAsync(cancellationToken);
        logger.LogDebug("Persistida muestra de métricas para VM {VmId} @ {Timestamp}",
            notification.VmId, notification.Snapshot.Timestamp);
    }
}

internal sealed class ContainersReportedHandler(MetricsDbContext db, ILogger<ContainersReportedHandler> logger)
    : INotificationHandler<ContainersReportedEvent>
{
    public async Task Handle(ContainersReportedEvent notification, CancellationToken cancellationToken)
    {
        db.ContainerSnapshots.Add(ContainerSnapshotRecord.FromSnapshot(notification.VmId, notification.Snapshot));
        await db.SaveChangesAsync(cancellationToken);
        logger.LogDebug("Persistido snapshot de {Count} contenedores para VM {VmId}",
            notification.Snapshot.Containers.Count, notification.VmId);
    }
}
