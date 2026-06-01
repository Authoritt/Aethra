using Aethra.Modules.Deployments.Infrastructure;
using Aethra.Modules.Deployments.UseCases.Deployment.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Deployments.UseCases.Deployment.Queries;

/// <summary>
/// Lista los deployments de una Instance ordenados por más recientes primero. <paramref name="Limit"/>
/// se acota a [1, 200] para evitar consultas accidentales sobre tablas grandes.
/// </summary>
public sealed record ListDeploymentsQuery(string InstanceId, int Limit = 50)
    : IQuery<IReadOnlyList<DeploymentSummaryDto>>;

internal sealed class ListDeploymentsHandler(DeploymentsDbContext db)
    : IQueryHandler<ListDeploymentsQuery, IReadOnlyList<DeploymentSummaryDto>>
{
    public async Task<Result<IReadOnlyList<DeploymentSummaryDto>>> Handle(
        ListDeploymentsQuery request, CancellationToken ct)
    {
        var limit = Math.Clamp(request.Limit, 1, 200);

        var deployments = await db.Deployments
            .AsNoTracking()
            .Where(d => d.InstanceId == request.InstanceId)
            .OrderByDescending(d => d.CreatedAt)
            .Take(limit)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var dtos = deployments.Select(DeploymentDtoMapper.ToSummary).ToList();
        return Result.Success<IReadOnlyList<DeploymentSummaryDto>>(dtos);
    }
}
