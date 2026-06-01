using Aethra.Modules.Deployments.Domain.Deployment;
using Aethra.Modules.Deployments.Infrastructure;
using Aethra.Modules.Deployments.UseCases.Deployment.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Deployments.UseCases.Deployment.Queries;

/// <summary>
/// Devuelve el detalle de un deployment por su ID. No incluye los logs — esos se piden
/// aparte vía <see cref="GetDeploymentLogsQuery"/> para que la UI los pagine.
/// </summary>
public sealed record GetDeploymentByIdQuery(string DeploymentId) : IQuery<DeploymentSummaryDto>;

internal sealed class GetDeploymentByIdHandler(DeploymentsDbContext db)
    : IQueryHandler<GetDeploymentByIdQuery, DeploymentSummaryDto>
{
    public async Task<Result<DeploymentSummaryDto>> Handle(GetDeploymentByIdQuery request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.DeploymentId, out var parsed) || parsed.Value.Prefix != "dep")
        {
            return Error.Validation("deployment.invalid_id", "ID de deployment inválido.");
        }
        var typed = new DeploymentId(parsed.Value);

        var deployment = await db.Deployments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == typed, ct)
            .ConfigureAwait(false);
        if (deployment is null)
        {
            return Error.NotFound("deployment.not_found",
                $"Deployment '{request.DeploymentId}' no existe.");
        }

        return DeploymentDtoMapper.ToSummary(deployment);
    }
}
