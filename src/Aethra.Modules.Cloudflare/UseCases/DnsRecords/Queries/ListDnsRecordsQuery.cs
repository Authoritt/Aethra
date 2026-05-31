using Aethra.Modules.Cloudflare.Application.Dtos;
using Aethra.Modules.Cloudflare.Application.Mapping;
using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Cloudflare.UseCases.DnsRecords.Queries;

public sealed record ListDnsRecordsQuery(string ZoneId) : IQuery<IReadOnlyList<DnsRecordDto>>;

internal sealed class ListDnsRecordsHandler(CloudflareDbContext db)
    : IQueryHandler<ListDnsRecordsQuery, IReadOnlyList<DnsRecordDto>>
{
    public async Task<Result<IReadOnlyList<DnsRecordDto>>> Handle(ListDnsRecordsQuery request, CancellationToken ct)
    {
        var idResult = IdParsing.ParseZoneId(request.ZoneId);
        if (idResult.IsFailure)
        {
            return idResult.Error;
        }
        var zoneId = idResult.Value;

        var exists = await db.Zones.AsNoTracking().AnyAsync(z => z.Id == zoneId, ct).ConfigureAwait(false);
        if (!exists)
        {
            return Error.NotFound("cloudflare.zone_not_found", $"Zona '{request.ZoneId}' no existe.");
        }

        var records = await db.DnsRecords
            .AsNoTracking()
            .Where(r => r.ZoneId == zoneId)
            .OrderBy(r => r.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var dtos = records.Select(CloudflareMappers.ToDto).ToList();
        return Result.Success<IReadOnlyList<DnsRecordDto>>(dtos);
    }
}
