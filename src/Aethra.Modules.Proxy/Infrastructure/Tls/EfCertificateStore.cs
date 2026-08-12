// NOTA DE WIRING (para Johan/compañero):
// Esta clase usa el DbContext concreto del módulo (ProxyDbContext) vía la interfaz Set<T>().
// Cuando crees ProxyDbContext con los DbSet<Certificate> y DbSet<AcmeAccount>, registra:
//   services.AddScoped<ICertificateStore>(sp => new EfCertificateStore(sp.GetRequiredService<ProxyDbContext>()));
// Eso lo hace AddAethraTls automáticamente si encuentra ProxyDbContext registrado — ver TlsRegistrationExtensions.

using Aethra.Modules.Proxy.Domain;
using Aethra.Shared.Kernel.Primitives;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Proxy.Infrastructure.Tls;

/// <summary>
/// Implementación EF de <see cref="ICertificateStore"/> sobre un <see cref="DbContext"/> arbitrario.
/// Se inyecta el ProxyDbContext cuando esté disponible. Cualquier <see cref="DbContext"/> que
/// exponga <c>DbSet&lt;Certificate&gt;</c> y <c>DbSet&lt;AcmeAccount&gt;</c> sirve.
/// </summary>
public sealed class EfCertificateStore(DbContext db) : ICertificateStore
{
    private DbSet<Certificate> Certificates => db.Set<Certificate>();
    private DbSet<AcmeAccount> Accounts => db.Set<AcmeAccount>();

    public Task<Certificate?> FindByIdAsync(CertificateId id, CancellationToken ct)
        => Certificates.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<Certificate?> FindByHostnameAsync(Hostname hostname, CancellationToken ct)
        => Certificates.FirstOrDefaultAsync(c => c.Hostname == hostname, ct);

    public async Task<IReadOnlyList<Certificate>> ListDueForRenewalAttemptAsync(DateTimeOffset now, CancellationToken ct)
    {
        var list = await Certificates
            // Expired incluido: un certificado caducado sigue siendo elegible para recuperarse.
            // Excluirlo lo dejaria muerto para siempre y el host sin TLS, sin reintento alguno.
            .Where(c => (c.Status == CertificateStatus.Issued
                         || c.Status == CertificateStatus.Failed
                         || c.Status == CertificateStatus.Expired)
                        && c.RenewAfter != null
                        && c.NotAfter != null
                        && c.RenewAfter <= now)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return list;
    }

    public async Task AddAsync(Certificate certificate, CancellationToken ct)
    {
        await Certificates.AddAsync(certificate, ct).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public Task<AcmeAccount?> FindAccountAsync(CancellationToken ct)
        => Accounts.FirstOrDefaultAsync(a => a.Id == AcmeAccount.DefaultId, ct);

    public async Task AddAccountAsync(AcmeAccount account, CancellationToken ct)
    {
        await Accounts.AddAsync(account, ct).ConfigureAwait(false);
    }
}
