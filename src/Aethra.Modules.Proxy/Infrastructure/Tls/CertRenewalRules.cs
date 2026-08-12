using Aethra.Modules.Proxy.Domain;

namespace Aethra.Modules.Proxy.Infrastructure.Tls;

public enum CertRenewalDecision
{
    Skip = 0,
    Renew = 1,
    Expire = 2,
}

public static class CertRenewalRules
{
    public static CertRenewalDecision Decide(
        CertificateStatus status,
        DateTimeOffset? renewAfter,
        DateTimeOffset? notAfter,
        DateTimeOffset now)
    {
        // Expired entra aquí a propósito. Un certificado caducado es el que MÁS necesita renovarse:
        // dejarlo fuera lo mataría para siempre y el host se quedaría sin TLS sin que nadie lo
        // reintentara nunca. Lo que este issue pedía era dejar de emitir el evento una y otra vez,
        // no dejar de renovar.
        if (status is not (CertificateStatus.Issued or CertificateStatus.Failed or CertificateStatus.Expired))
        {
            return CertRenewalDecision.Skip;
        }

        if (renewAfter is null || renewAfter > now)
        {
            return CertRenewalDecision.Skip;
        }

        // El evento de expiración se emite UNA vez: en la transición a Expired. Si ya estaba
        // Expired, la caducidad no es noticia — lo que toca es seguir intentando recuperarlo.
        if (notAfter is { } expiresAt && expiresAt <= now && status != CertificateStatus.Expired)
        {
            return CertRenewalDecision.Expire;
        }

        return CertRenewalDecision.Renew;
    }

    public static DateTimeOffset NextRetryAfter(DateTimeOffset now, TimeSpan backoff)
    {
        if (backoff <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(backoff), "Backoff must be positive.");
        }

        return now + backoff;
    }
}
