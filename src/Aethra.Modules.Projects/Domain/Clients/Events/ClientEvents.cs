using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Projects.Domain.Clients.Events;

/// <summary>
/// Disparado al crear un <see cref="Client"/> (tenant) dentro de un Project.
/// </summary>
public sealed record ClientCreatedEvent(
    ClientId ClientId,
    ProjectId ProjectId,
    string Slug,
    string DisplayName) : DomainEvent;

/// <summary>
/// Disparado cuando se actualiza información administrativa de un Client
/// (display name, descripción, contacto, billing tag).
/// </summary>
public sealed record ClientInfoUpdatedEvent(
    ClientId ClientId,
    string DisplayName) : DomainEvent;
