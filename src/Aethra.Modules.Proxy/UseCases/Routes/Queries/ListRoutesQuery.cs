using Aethra.Modules.Proxy.Infrastructure;
using Aethra.Modules.Proxy.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Proxy.UseCases.Routes.Queries;

public sealed record ListRoutesQuery : IQuery<IReadOnlyList<RouteDto>>;

internal sealed class ListRoutesHandler(ProxyDbContext db) : IQueryHandler<ListRoutesQuery, IReadOnlyList<RouteDto>>
{
    public async Task<Result<IReadOnlyList<RouteDto>>> Handle(ListRoutesQuery request, CancellationToken ct)
    {
        var routes = await db.Routes.AsNoTracking().OrderBy(r => r.Hostname).ToListAsync(ct);
        var certs = await db.Certificates.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);

        var dtos = routes.Select(r =>
        {
            var status = "none";
            DateTimeOffset? expires = null;
            if (r.TlsEnabled)
            {
                if (r.CertificateId is { } certId && certs.TryGetValue(certId, out var cert))
                {
                    status = cert.Status.ToString().ToLowerInvariant();
                    expires = cert.NotAfter;
                }
                else
                {
                    status = "pending";
                }
            }
            return RouteMapper.ToDto(r, status, expires);
        }).ToList();

        return Result.Success<IReadOnlyList<RouteDto>>(dtos);
    }
}
