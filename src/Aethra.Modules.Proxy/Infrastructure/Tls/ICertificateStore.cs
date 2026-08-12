using Aethra.Modules.Proxy.Domain;
using Aethra.Shared.Kernel.Primitives;

namespace Aethra.Modules.Proxy.Infrastructure.Tls;

/// <summary>
/// Fachada sobre <c>ProxyDbContext</c> para las entidades TLS. Se introduce esta interfaz para
/// que <see cref="LetsEncryptCertManager"/> no se acople al DbContext concreto (que es creado y
/// owneado por el módulo Proxy / rutas). La implementación EF vive en <c>EfCertificateStore</c>.
/// </summary>
public interface ICertificateStore
{
    Task<Certificate?> FindByIdAsync(CertificateId id, CancellationToken ct);
    Task<Certificate?> FindByHostnameAsync(Hostname hostname, CancellationToken ct);
    Task<IReadOnlyList<Certificate>> ListDueForRenewalAttemptAsync(DateTimeOffset now, CancellationToken ct);
    Task AddAsync(Certificate certificate, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);

    Task<AcmeAccount?> FindAccountAsync(CancellationToken ct);
    Task AddAccountAsync(AcmeAccount account, CancellationToken ct);
}
