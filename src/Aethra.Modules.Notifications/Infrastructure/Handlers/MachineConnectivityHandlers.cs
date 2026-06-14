using Aethra.Modules.Notifications.Domain;
using Aethra.Shared.Contracts.Vms;
using MediatR;

namespace Aethra.Modules.Notifications.Infrastructure.Handlers;

/// <summary>
/// Listener cross-module: traduce la (des)conexión de un satélite a notificaciones
/// <c>machine.disconnected</c> (un nodo se cayó — evento operacional crítico que antes sólo
/// se veía en la UI) y <c>machine.connected</c> (recuperación). Los eventos ya viajan por el
/// integration bus desde <c>SatelliteHub</c>; aquí sólo se hace el fan-out a los canales.
/// </summary>
internal sealed class SatelliteDisconnectedHandler(NotificationEventDispatcher dispatcher)
    : INotificationHandler<SatelliteDisconnectedEvent>
{
    public Task Handle(SatelliteDisconnectedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return dispatcher.DispatchAsync(
            NotificationEventTypes.MachineDisconnected,
            new
            {
                vm_id = notification.VmId,
                reason = notification.Reason,
                timestamp = notification.OccurredAt,
            },
            cancellationToken);
    }
}

internal sealed class SatelliteConnectedHandler(NotificationEventDispatcher dispatcher)
    : INotificationHandler<SatelliteConnectedEvent>
{
    public Task Handle(SatelliteConnectedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return dispatcher.DispatchAsync(
            NotificationEventTypes.MachineConnected,
            new
            {
                vm_id = notification.VmId,
                hostname = notification.Hostname,
                timestamp = notification.OccurredAt,
            },
            cancellationToken);
    }
}
