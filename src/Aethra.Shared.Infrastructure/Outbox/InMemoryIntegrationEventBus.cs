using Aethra.Shared.Kernel.Domain;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Aethra.Shared.Infrastructure.Outbox;

/// <summary>
/// Implementacion en proceso: simplemente publica via MediatR como INotification.
/// Los suscriptores cross-module implementan INotificationHandler&lt;TEvent&gt;.
///
/// Cuando un dia haya que separar modulos en procesos distintos, basta reemplazar esta
/// implementacion por una que escriba a Kafka/NATS/RabbitMQ sin tocar handlers.
/// </summary>
public sealed class InMemoryIntegrationEventBus(IMediator mediator, ILogger<InMemoryIntegrationEventBus> logger)
    : IIntegrationEventBus
{
    public async Task PublishAsync(IIntegrationEvent @event, CancellationToken ct)
    {
        if (@event is INotification notification)
        {
            await mediator.Publish(notification, ct).ConfigureAwait(false);
            logger.LogDebug("Integration event publicado: {EventType} {EventId}",
                @event.GetType().Name, @event.EventId);
            return;
        }

        logger.LogWarning(
            "Integration event {EventType} no implementa INotification — sera ignorado por el bus.",
            @event.GetType().FullName);
    }
}
