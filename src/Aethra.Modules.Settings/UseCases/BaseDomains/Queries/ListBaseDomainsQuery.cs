using Aethra.Modules.Settings.Infrastructure;
using Aethra.Modules.Settings.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Settings.UseCases.BaseDomains.Queries;

public sealed record ListBaseDomainsQuery : IQuery<IReadOnlyList<BaseDomainDto>>;

internal sealed class ListBaseDomainsHandler(SettingsDbContext db)
    : IQueryHandler<ListBaseDomainsQuery, IReadOnlyList<BaseDomainDto>>
{
    public async Task<Result<IReadOnlyList<BaseDomainDto>>> Handle(
        ListBaseDomainsQuery request,
        CancellationToken cancellationToken)
    {
        var rows = await db.BaseDomains
            .AsNoTracking()
            .OrderByDescending(d => d.IsActive)
            .ThenBy(d => d.Hostname)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<BaseDomainDto> dtos = [.. rows.Select(Mappers.ToDto)];
        return Result.Success(dtos);
    }
}
