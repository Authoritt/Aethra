namespace Aethra.Shared.Contracts.Proxy;

/// <summary>
/// Un certificado TLS fue emitido (primera vez) por la autoridad ACME.
/// Consumido por el módulo Proxy para recargar la config de YARP y por
/// cualquier otro suscriptor interesado (auditoría, métricas).
/// </summary>
public sealed record CertificateIssuedEvent(
    string CertificateId,
    string Hostname,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter
) : IntegrationEvent;
