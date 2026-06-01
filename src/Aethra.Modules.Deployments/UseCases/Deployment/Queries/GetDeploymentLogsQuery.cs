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
/// Devuelve las líneas del log de un deployment a partir de <paramref name="Since"/> (inclusivo).
/// La UI hace polling incremental con la última sequence vista para no recibir las líneas ya
/// pintadas.
/// </summary>
public sealed record GetDeploymentLogsQuery(string DeploymentId, long Since = 0)
    : IQuery<IReadOnlyList<DeploymentLogChunkDto>>;

internal sealed class GetDeploymentLogsHandler(DeploymentsDbContext db)
    : IQueryHandler<GetDeploymentLogsQuery, IReadOnlyList<DeploymentLogChunkDto>>
{
    public async Task<Result<IReadOnlyList<DeploymentLogChunkDto>>> Handle(
        GetDeploymentLogsQuery request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.DeploymentId, out var parsed) || parsed.Value.Prefix != "dep")
        {
            return Error.Validation("deployment.invalid_id", "ID de deployment inválido.");
        }
        var typed = new DeploymentId(parsed.Value);

        var logs = await db.DeploymentLogs
            .AsNoTracking()
            .Where(l => l.DeploymentId == typed && l.Sequence >= request.Since)
            .OrderBy(l => l.Sequence)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var dtos = logs.Select(l => new DeploymentLogChunkDto(
            DeploymentId: l.DeploymentId.ToString(),
            Sequence: l.Sequence,
            Timestamp: l.Timestamp,
            Level: l.Level.ToString().ToLowerInvariant(),
            Stage: l.Stage,
            Text: l.Text)).ToList();

        return Result.Success<IReadOnlyList<DeploymentLogChunkDto>>(dtos);
    }
}
