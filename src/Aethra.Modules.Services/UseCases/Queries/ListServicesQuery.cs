using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.UseCases.Dtos;
using Aethra.Modules.Services.UseCases.Mapping;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Services.UseCases.Queries;

public sealed record ListServicesQuery() : IQuery<IReadOnlyList<ManagedServiceSummaryDto>>;

internal sealed class ListServicesHandler(ServicesDbContext db)
    : IQueryHandler<ListServicesQuery, IReadOnlyList<ManagedServiceSummaryDto>>
{
    public async Task<Result<IReadOnlyList<ManagedServiceSummaryDto>>> Handle(ListServicesQuery request, CancellationToken cancellationToken)
    {
        var services = await db.ManagedServices.AsNoTracking()
            .OrderBy(s => s.Slug)
            .ToListAsync(cancellationToken);
        var bindingCounts = await db.ServiceBindings.AsNoTracking()
            .Where(b => b.RevokedAt == null)
            .GroupBy(b => b.ServiceId)
            .Select(g => new { ServiceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ServiceId, g => g.Count, cancellationToken);

        IReadOnlyList<ManagedServiceSummaryDto> dtos = [.. services.Select(s =>
            ServiceMappers.ToSummary(s, bindingCounts.GetValueOrDefault(s.Id)))];
        return Result.Success(dtos);
    }
}
