using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Settings.Domain.Events;

/// <summary>
/// Eventos de dominio que emiten los aggregates de Settings. Se publican dentro de la
/// misma transacción de <c>SaveChanges</c>. Ningún evento expone valores en texto plano
/// — solo metadata (nombre, id) para que el audit log no exfiltre secretos.
/// </summary>
public sealed record IntegrationCredentialCreatedEvent(
    IntegrationCredentialId CredentialId,
    string Name,
    IntegrationCredentialType Type) : DomainEvent;

public sealed record IntegrationCredentialRotatedEvent(
    IntegrationCredentialId CredentialId,
    string Name) : DomainEvent;

public sealed record BaseDomainCreatedEvent(
    BaseDomainId BaseDomainId,
    string Hostname) : DomainEvent;

public sealed record BaseDomainActivatedEvent(
    BaseDomainId BaseDomainId,
    string Hostname) : DomainEvent;
