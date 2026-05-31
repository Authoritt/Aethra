namespace Aethra.Shared.Contracts.Proxy;

/// <summary>
/// Un certificado TLS expiró sin poder renovarse a tiempo (todos los reintentos del worker fallaron).
/// El módulo Proxy debe degradar la ruta TLS asociada y/o alertar al operador.
/// </summary>
public sealed record CertificateExpiredEvent(
    string CertificateId,
    string Hostname,
    DateTimeOffset ExpiredAt,
    string? LastError
) : IntegrationEvent;
