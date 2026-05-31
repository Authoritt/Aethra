using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Proxy.Domain.Events;

/// <summary>
/// Eventos de DOMINIO del agregado <see cref="Certificate"/>. Distintos de los
/// <c>IntegrationEvent</c>s en <c>Aethra.Shared.Contracts.Proxy</c>: estos no cruzan
/// la frontera del módulo y se publican intra-proceso vía MediatR.
/// </summary>
public sealed record CertificateRequestedEvent(CertificateId CertificateId, string Hostname) : DomainEvent;

public sealed record CertificateIssuedDomainEvent(
    CertificateId CertificateId,
    string Hostname,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter) : DomainEvent;

public sealed record CertificateRenewedDomainEvent(
    CertificateId CertificateId,
    string Hostname,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter) : DomainEvent;

public sealed record CertificateFailedEvent(
    CertificateId CertificateId,
    string Hostname,
    string Error) : DomainEvent;
