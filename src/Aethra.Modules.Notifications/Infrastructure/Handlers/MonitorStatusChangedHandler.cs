using Aethra.Modules.Notifications.Domain;
using Aethra.Shared.Contracts.Monitoring;
using MediatR;

namespace Aethra.Modules.Notifications.Infrastructure.Handlers;

/// <summary>
/// Listener cross-module: traduce <see cref="MonitorStatusChangedIntegrationEvent"/> a
/// notificaciones <c>monitor.down</c> o <c>monitor.recovered</c> segun la transicion. Otros
/// estados (Degraded, Unknown) no generan notificacion para evitar ruido.
/// </summary>
internal sealed class MonitorStatusChangedHandler(NotificationEventDispatcher dispatcher)
    : INotificationHandler<MonitorStatusChangedIntegrationEvent>
{
    public async Task Handle(MonitorStatusChangedIntegrationEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        // Transicion a Down: alerta.
        if (string.Equals(notification.To, "Down", StringComparison.OrdinalIgnoreCase))
        {
            await dispatcher.DispatchAsync(
                NotificationEventTypes.MonitorDown,
                new
                {
                    monitor_id = notification.MonitorId,
                    from = notification.From,
                    to = notification.To,
                    check_id = notification.CheckId,
                    http_status_code = notification.HttpStatusCode,
                    latency_ms = notification.LatencyMs,
                    timestamp = notification.Timestamp,
                },
                cancellationToken).ConfigureAwait(false);
            return;
        }

        // Recovery: Up tras Down.
        if (string.Equals(notification.To, "Up", StringComparison.OrdinalIgnoreCase)
            && string.Equals(notification.From, "Down", StringComparison.OrdinalIgnoreCase))
        {
            await dispatcher.DispatchAsync(
                NotificationEventTypes.MonitorRecovered,
                new
                {
                    monitor_id = notification.MonitorId,
                    from = notification.From,
                    to = notification.To,
                    check_id = notification.CheckId,
                    http_status_code = notification.HttpStatusCode,
                    latency_ms = notification.LatencyMs,
                    timestamp = notification.Timestamp,
                },
                cancellationToken).ConfigureAwait(false);
        }
    }
}
