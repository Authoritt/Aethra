using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Identity.Domain.Events;

/// <summary>
/// Eventos de dominio que emite el agregado <see cref="ApiKey"/>. Se publican dentro
/// de la misma transacción de <c>SaveChanges</c> y pueden ser proyectados a integration
/// events posteriormente (audit log, alertas) si una fase futura lo requiere.
/// </summary>
public sealed record ApiKeyCreatedEvent(ApiKeyId ApiKeyId, string Name, IReadOnlyList<string> Scopes) : DomainEvent;

public sealed record ApiKeyRevokedEvent(ApiKeyId ApiKeyId, string Name) : DomainEvent;
