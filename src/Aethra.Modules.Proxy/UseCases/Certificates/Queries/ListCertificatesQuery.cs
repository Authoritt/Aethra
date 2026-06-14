using Aethra.Modules.Proxy.Infrastructure;
using Aethra.Modules.Proxy.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Proxy.UseCases.Certificates.Queries;

/// <summary>Lista los certificados TLS gestionados (metadata/estado; sin PEM ni clave).</summary>
public sealed record ListCertificatesQuery : IQuery<IReadOnlyList<CertificateDto>>;

internal sealed class ListCertificatesHandler(ProxyDbContext db)
    : IQueryHandler<ListCertificatesQuery, IReadOnlyList<CertificateDto>>
{
    public async Task<Result<IReadOnlyList<CertificateDto>>> Handle(ListCertificatesQuery request, CancellationToken ct)
    {
        var dtos = await db.Certificates
            .AsNoTracking()
            .OrderBy(c => c.Hostname)
            .Select(c => new CertificateDto(
                c.Id.ToString(),
                c.Hostname.Value,
                c.Status.ToString().ToLowerInvariant(),
                c.IssuedAt,
                c.NotBefore,
                c.NotAfter,
                c.RenewAfter,
                c.LastError))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<CertificateDto>>(dtos);
    }
}
