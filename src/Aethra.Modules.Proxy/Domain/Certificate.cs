using Aethra.Modules.Proxy.Domain.Events;
using Aethra.Shared.Kernel.Domain;
using Aethra.Shared.Kernel.Primitives;

namespace Aethra.Modules.Proxy.Domain;

/// <summary>
/// Certificado TLS emitido por una CA ACME (Let's Encrypt) para un <see cref="Hostname"/>.
/// El PFX (cert + private key) se persiste cifrado en <see cref="PfxCipherText"/> mediante
/// DataProtection con propósito <c>"aethra-cert-pfx"</c>. <see cref="RenewAfter"/> se calcula
/// como <c>NotAfter - RenewBeforeDays</c> (default 30) y guía al <c>CertRenewalWorker</c>.
/// </summary>
public sealed class Certificate : AggregateRoot<CertificateId>
{
    public Hostname Hostname { get; private set; }
    public CertificateStatus Status { get; private set; }

    /// <summary>
    /// PFX (cert + private key) serializado y luego cifrado con DataProtection.
    /// Almacenado como base64 del ciphertext. Nunca devolver crudo fuera del módulo.
    /// </summary>
    public string? PfxCipherText { get; private set; }

    public DateTimeOffset? IssuedAt { get; private set; }
    public DateTimeOffset? NotBefore { get; private set; }
    public DateTimeOffset? NotAfter { get; private set; }
    public DateTimeOffset? RenewAfter { get; private set; }
    public string? LastError { get; private set; }

    private Certificate(CertificateId id, Hostname hostname) : base(id)
    {
        Hostname = hostname;
        Status = CertificateStatus.Pending;
    }

    /// <summary>
    /// Crea una solicitud nueva en estado <see cref="CertificateStatus.Pending"/>. La emisión
    /// efectiva la dispara el <c>ICertManager</c> tras el desafío ACME.
    /// </summary>
    public static Certificate Request(Hostname hostname)
    {
        var cert = new Certificate(CertificateId.New(), hostname);
        cert.Raise(new CertificateRequestedEvent(cert.Id, hostname.Value));
        return cert;
    }

    /// <summary>
    /// Marca el certificado como emitido tras una respuesta ACME exitosa.
    /// </summary>
    public void MarkIssued(
        string pfxCipherText,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter,
        int renewBeforeDays,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pfxCipherText);
        if (renewBeforeDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(renewBeforeDays), "Debe ser > 0.");
        }

        var wasIssued = Status == CertificateStatus.Issued
            || Status == CertificateStatus.Renewing
            || Status == CertificateStatus.Expired
            || (Status == CertificateStatus.Failed && PfxCipherText is not null);

        PfxCipherText = pfxCipherText;
        Status = CertificateStatus.Issued;
        IssuedAt = now;
        NotBefore = notBefore;
        NotAfter = notAfter;
        RenewAfter = notAfter - TimeSpan.FromDays(renewBeforeDays);
        LastError = null;

        if (wasIssued)
        {
            Raise(new CertificateRenewedDomainEvent(Id, Hostname.Value, notBefore, notAfter));
        }
        else
        {
            Raise(new CertificateIssuedDomainEvent(Id, Hostname.Value, notBefore, notAfter));
        }
    }

    /// <summary>
    /// Cambia el estado a <see cref="CertificateStatus.Renewing"/>. El cert anterior sigue
    /// siendo el válido hasta que <see cref="MarkIssued"/> entregue el nuevo PFX.
    /// </summary>
    public void MarkRenewing()
    {
        if (Status != CertificateStatus.Issued
            && Status != CertificateStatus.Failed
            && Status != CertificateStatus.Expired)
        {
            throw new InvalidOperationException(
                $"Sólo se puede renovar un cert Issued/Failed. Estado actual: {Status}.");
        }
        Status = CertificateStatus.Renewing;
    }

    /// <summary>
    /// Registra un fallo de emisión/renovación. No invalida el PFX existente: si había un cert
    /// previamente emitido sigue presente; el operador o el worker decidirán cuándo reintentar.
    /// </summary>
    public void MarkFailed(string error)
    {
        Status = CertificateStatus.Failed;
        LastError = error;
        Raise(new CertificateFailedEvent(Id, Hostname.Value, error));
    }

    public void ScheduleRenewalRetry(DateTimeOffset retryAfter)
    {
        if (Status != CertificateStatus.Failed)
        {
            throw new InvalidOperationException(
                $"Solo se puede programar retry para un cert Failed. Estado actual: {Status}.");
        }

        RenewAfter = retryAfter;
    }

    public void MarkExpired()
    {
        if (Status != CertificateStatus.Issued && Status != CertificateStatus.Failed)
        {
            throw new InvalidOperationException(
                $"Solo se puede expirar un cert Issued/Failed. Estado actual: {Status}.");
        }

        Status = CertificateStatus.Expired;
        RenewAfter = null;
    }

    // EF Core requiere un ctor sin parámetros para materializar.
    private Certificate() : base()
    {
        // Hostname es value object con default => EF reemplaza vía reflexión.
        Hostname = default;
    }
}
