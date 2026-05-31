using Aethra.Shared.Kernel.Domain;

namespace Aethra.Shared.Infrastructure.Outbox;

/// <summary>
/// Bus in-memory de eventos de integracion. El dispatcher publica aqui despues de leer
/// del outbox, y los suscriptores (handlers MediatR de tipo INotificationHandler&lt;TEvent&gt;)
/// consumen.
/// </summary>
public interface IIntegrationEventBus
{
    Task PublishAsync(IIntegrationEvent @event, CancellationToken ct);
}
