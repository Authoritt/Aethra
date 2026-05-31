namespace Aethra.Shared.Kernel.Domain;

/// <summary>
/// Evento que ocurre dentro del agregado y se publica dentro de la misma transacción.
/// Para comunicación cross-module use <see cref="IIntegrationEvent"/>.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Base abstracta para eventos de dominio con ID y timestamp autogenerados.
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
