using System.Security.Cryptography.X509Certificates;
using Aethra.Modules.Proxy.Domain;
using Aethra.Shared.Kernel.Primitives;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Modules.Proxy.Infrastructure.Tls;

/// <summary>
/// Orquesta el ciclo de vida de los certificados TLS contra una CA ACME (Let's Encrypt en F3).
/// Las operaciones devuelven <see cref="Result{T}"/> para que el caller (worker, command handler)
/// decida qué hacer con el error sin atrapar excepciones de red.
/// </summary>
public interface ICertManager
{
    /// <summary>
    /// Pide un certificado nuevo para <paramref name="hostname"/>. Crea el agregado en estado
    /// <see cref="CertificateStatus.Pending"/>, ejecuta el desafío HTTP-01, finaliza la orden y
    /// persiste el PFX cifrado. El <see cref="Certificate"/> devuelto queda en estado <c>Issued</c>.
    /// </summary>
    Task<Result<Certificate>> RequestAsync(Hostname hostname, CancellationToken ct);

    /// <summary>Renueva un certificado existente. Reusa la cuenta ACME y reemplaza el PFX.</summary>
    Task<Result<Certificate>> RenewAsync(CertificateId id, CancellationToken ct);

    /// <summary>
    /// Descifra el PFX en BD y devuelve un <see cref="X509Certificate2"/> listo para montar en
    /// Kestrel/YARP. Devuelve <c>null</c> si el cert no existe o aún no fue emitido.
    /// </summary>
    Task<X509Certificate2?> LoadCertAsync(CertificateId id, CancellationToken ct);
}
