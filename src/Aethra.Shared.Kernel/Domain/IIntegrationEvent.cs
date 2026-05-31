namespace Aethra.Shared.Kernel.Domain;

/// <summary>
/// Evento publicado cross-module vía Outbox.
/// Garantía at-least-once, ordering por agregado, no por sistema.
///
/// Implementaciones concretas viven en Aethra.Shared.Contracts (records puros sin dependencias).
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}
