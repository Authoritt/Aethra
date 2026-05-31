namespace Aethra.Shared.Contracts.Proxy;

/// <summary>
/// Un certificado TLS existente fue renovado. La nueva fecha de expiración va en NotAfter.
/// </summary>
public sealed record CertificateRenewedEvent(
    string CertificateId,
    string Hostname,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter
) : IntegrationEvent;
