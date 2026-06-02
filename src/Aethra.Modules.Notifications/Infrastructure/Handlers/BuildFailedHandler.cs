using Aethra.Modules.Notifications.Domain;
using Aethra.Shared.Contracts.Deployments;
using MediatR;

namespace Aethra.Modules.Notifications.Infrastructure.Handlers;

internal sealed class BuildFailedHandler(NotificationEventDispatcher dispatcher)
    : INotificationHandler<BuildFailedIntegrationEvent>
{
    public Task Handle(BuildFailedIntegrationEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return dispatcher.DispatchAsync(
            NotificationEventTypes.BuildFailed,
            new
            {
                build_id = notification.BuildId,
                template_id = notification.TemplateId,
                error_code = notification.ErrorCode,
                error_message = notification.ErrorMessage,
                failed_at = notification.FailedAt,
            },
            cancellationToken);
    }
}
