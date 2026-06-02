namespace Aethra.Shared.Contracts.Proxy;

/// <summary>
/// Falló la emisión o renovación de un certificado TLS vía ACME (Let's Encrypt).
/// A diferencia de <see cref="CertificateExpiredEvent"/> — que se emite cuando un cert
/// previamente emitido cruzó su <c>NotAfter</c> sin reemplazo — éste se emite tras un
/// intento concreto fallido (DNS, HTTP-01, rate limit, timeout, etc).
///
/// Consumidores típicos: módulos Monitoring (sube alerta), Notes (deja PinnedFact con el
/// último error visible al operador) y cualquier auditoría/log centralizado.
///
/// <para>
/// Se publica desde <c>LetsEncryptCertManager.IssueAsync</c> en el catch general antes
/// de persistir <c>cert.MarkFailed(...)</c>. Es independiente del domain event
/// <c>CertificateFailedEvent</c> (intra-módulo, MediatR): este integration event sí cruza
/// la frontera del bounded context vía outbox at-least-once.
/// </para>
/// </summary>
public sealed record CertificateFailedEvent(
    string CertificateId,
    string Hostname,
    string ErrorMessage
) : IntegrationEvent;
