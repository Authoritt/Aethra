using Aethra.Modules.Cloudflare.Application.Dtos;
using Aethra.Modules.Cloudflare.Application.Mapping;
using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Cloudflare.UseCases.Zones.Queries;

public sealed record GetZoneByIdQuery(string ZoneId) : IQuery<CloudflareZoneDetailDto>;

internal sealed class GetZoneByIdHandler(CloudflareDbContext db)
    : IQueryHandler<GetZoneByIdQuery, CloudflareZoneDetailDto>
{
    public async Task<Result<CloudflareZoneDetailDto>> Handle(GetZoneByIdQuery request, CancellationToken ct)
    {
        var idResult = IdParsing.ParseZoneId(request.ZoneId);
        if (idResult.IsFailure)
        {
            return idResult.Error;
        }
        var zoneId = idResult.Value;

        var zone = await db.Zones.AsNoTracking().FirstOrDefaultAsync(z => z.Id == zoneId, ct).ConfigureAwait(false);
        if (zone is null)
        {
            return Error.NotFound("cloudflare.zone_not_found", $"Zona '{request.ZoneId}' no existe.");
        }
        var records = await db.DnsRecords
            .AsNoTracking()
            .Where(r => r.ZoneId == zoneId)
            .OrderBy(r => r.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return CloudflareMappers.ToDetail(zone, records);
    }
}
