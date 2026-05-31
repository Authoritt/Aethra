using Aethra.Shared.Kernel.Domain;
using MediatR;

namespace Aethra.Shared.Contracts;

/// <summary>
/// Base para eventos de integración cross-module.
/// Implementa <see cref="IIntegrationEvent"/> (semántica de Aethra) y <see cref="INotification"/>
/// (para que MediatR.Publish lo despache a los <c>INotificationHandler&lt;T&gt;</c> de los módulos consumidores).
/// </summary>
public abstract record IntegrationEvent : IIntegrationEvent, INotification
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
