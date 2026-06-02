using Aethra.Modules.Notifications.Domain;
using Aethra.Shared.Contracts.Proxy;
using MediatR;

namespace Aethra.Modules.Notifications.Infrastructure.Handlers;

internal sealed class CertificateExpiredHandler(NotificationEventDispatcher dispatcher)
    : INotificationHandler<CertificateExpiredEvent>
{
    public Task Handle(CertificateExpiredEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return dispatcher.DispatchAsync(
            NotificationEventTypes.CertificateExpired,
            new
            {
                certificate_id = notification.CertificateId,
                hostname = notification.Hostname,
                expired_at = notification.ExpiredAt,
                last_error = notification.LastError,
            },
            cancellationToken);
    }
}

internal sealed class CertificateFailedHandler(NotificationEventDispatcher dispatcher)
    : INotificationHandler<CertificateFailedEvent>
{
    public Task Handle(CertificateFailedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return dispatcher.DispatchAsync(
            NotificationEventTypes.CertificateFailed,
            new
            {
                certificate_id = notification.CertificateId,
                hostname = notification.Hostname,
                error_message = notification.ErrorMessage,
            },
            cancellationToken);
    }
}
