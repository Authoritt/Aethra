using Aethra.Modules.Deployments.Infrastructure;
using Aethra.Modules.Deployments.UseCases.Build.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Deployments.UseCases.Build.Queries;

/// <summary>
/// Lista los builds de un Template ordenados por más recientes primero. <paramref name="Limit"/>
/// se acota a [1, 200] para evitar consultas accidentales sobre tablas grandes.
/// </summary>
public sealed record ListBuildsQuery(string TemplateId, int Limit = 50)
    : IQuery<IReadOnlyList<BuildSummaryDto>>;

internal sealed class ListBuildsHandler(DeploymentsDbContext db)
    : IQueryHandler<ListBuildsQuery, IReadOnlyList<BuildSummaryDto>>
{
    public async Task<Result<IReadOnlyList<BuildSummaryDto>>> Handle(ListBuildsQuery request, CancellationToken ct)
    {
        var limit = Math.Clamp(request.Limit, 1, 200);

        var builds = await db.Builds
            .AsNoTracking()
            .Where(b => b.TemplateId == request.TemplateId)
            .OrderByDescending(b => b.CreatedAt)
            .Take(limit)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var dtos = builds.Select(BuildDtoMapper.ToSummary).ToList();
        return Result.Success<IReadOnlyList<BuildSummaryDto>>(dtos);
    }
}
