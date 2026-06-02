using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Identity.Domain.Events;

/// <summary>
/// Eventos de dominio del agregado <see cref="User"/>. Se publican dentro de la misma
/// transacción de <c>SaveChanges</c> y pueden proyectarse a integration events si
/// algún módulo (Notes, Deployments) quiere reaccionar a creación/desactivación.
/// </summary>
public sealed record UserCreatedEvent(UserId UserId, string Email) : DomainEvent;

public sealed record UserDeactivatedEvent(UserId UserId) : DomainEvent;

public sealed record UserPasswordResetEvent(UserId UserId) : DomainEvent;
