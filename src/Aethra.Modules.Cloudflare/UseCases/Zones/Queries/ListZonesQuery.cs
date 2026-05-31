using Aethra.Modules.Cloudflare.Application.Dtos;
using Aethra.Modules.Cloudflare.Application.Mapping;
using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Cloudflare.UseCases.Zones.Queries;

public sealed record ListZonesQuery : IQuery<IReadOnlyList<CloudflareZoneDto>>;

internal sealed class ListZonesHandler(CloudflareDbContext db)
    : IQueryHandler<ListZonesQuery, IReadOnlyList<CloudflareZoneDto>>
{
    public async Task<Result<IReadOnlyList<CloudflareZoneDto>>> Handle(ListZonesQuery request, CancellationToken ct)
    {
        var zones = await db.Zones.AsNoTracking().OrderBy(z => z.Name).ToListAsync(ct).ConfigureAwait(false);
        var counts = await db.DnsRecords
            .AsNoTracking()
            .GroupBy(r => r.ZoneId)
            .Select(g => new { ZoneId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ZoneId, x => x.Count, ct)
            .ConfigureAwait(false);

        var dtos = zones
            .Select(z => CloudflareMappers.ToSummary(z, counts.GetValueOrDefault(z.Id)))
            .ToList();
        return Result.Success<IReadOnlyList<CloudflareZoneDto>>(dtos);
    }
}
