using Aethra.Modules.Notifications.Domain;
using Aethra.Shared.Contracts.Deployments;
using MediatR;

namespace Aethra.Modules.Notifications.Infrastructure.Handlers;

internal sealed class DeploymentFailedHandler(NotificationEventDispatcher dispatcher)
    : INotificationHandler<DeploymentFailedIntegrationEvent>
{
    public Task Handle(DeploymentFailedIntegrationEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return dispatcher.DispatchAsync(
            NotificationEventTypes.DeploymentFailed,
            new
            {
                deployment_id = notification.DeploymentId,
                instance_id = notification.InstanceId,
                error_code = notification.ErrorCode,
                error_message = notification.ErrorMessage,
                failed_at = notification.FailedAt,
            },
            cancellationToken);
    }
}

internal sealed class DeploymentRolledBackHandler(NotificationEventDispatcher dispatcher)
    : INotificationHandler<DeploymentRolledBackIntegrationEvent>
{
    public Task Handle(DeploymentRolledBackIntegrationEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return dispatcher.DispatchAsync(
            NotificationEventTypes.DeploymentRolledBack,
            new
            {
                deployment_id = notification.DeploymentId,
                instance_id = notification.InstanceId,
                error_code = notification.ErrorCode,
                error_message = notification.ErrorMessage,
                rolled_back_at = notification.RolledBackAt,
            },
            cancellationToken);
    }
}
