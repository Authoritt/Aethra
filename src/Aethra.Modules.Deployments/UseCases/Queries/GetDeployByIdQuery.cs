using Aethra.Modules.Deployments.Domain;
using Aethra.Modules.Deployments.Infrastructure;
using Aethra.Modules.Deployments.UseCases.Dtos;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Deployments.UseCases.Queries;

public sealed record GetDeployByIdQuery(string JobId) : IQuery<DeployJobSummaryDto>;

internal sealed class GetDeployByIdHandler(DeploymentsDbContext db, IApplicationLookup lookup)
    : IQueryHandler<GetDeployByIdQuery, DeployJobSummaryDto>
{
    public async Task<Result<DeployJobSummaryDto>> Handle(GetDeployByIdQuery request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.JobId, out var parsed) || parsed.Value.Prefix != "dpl")
        {
            return Error.Validation("deploy.invalid_id", "ID de deploy inválido.");
        }
        var typed = new DeployJobId(parsed.Value);
        var job = await db.DeployJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == typed, ct);
        if (job is null)
        {
            return Error.NotFound("deploy.not_found", $"Deploy '{request.JobId}' no existe.");
        }
        var app = await lookup.GetByIdAsync(job.ApplicationId, ct);
        return ListDeploysHandler.MapToDto(job, app?.Slug ?? "unknown");
    }
}

public sealed record GetDeployLogsQuery(string JobId, long Since = 0) : IQuery<IReadOnlyList<DeployLogChunkDto>>;

internal sealed class GetDeployLogsHandler(DeploymentsDbContext db)
    : IQueryHandler<GetDeployLogsQuery, IReadOnlyList<DeployLogChunkDto>>
{
    public async Task<Result<IReadOnlyList<DeployLogChunkDto>>> Handle(GetDeployLogsQuery request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.JobId, out var parsed) || parsed.Value.Prefix != "dpl")
        {
            return Error.Validation("deploy.invalid_id", "ID de deploy inválido.");
        }
        var typed = new DeployJobId(parsed.Value);

        var logs = await db.DeployLogs
            .AsNoTracking()
            .Where(l => l.JobId == typed && l.Sequence >= request.Since)
            .OrderBy(l => l.Sequence)
            .ToListAsync(ct);

        var dtos = logs.Select(l => new DeployLogChunkDto(
            JobId: l.JobId.ToString(),
            Sequence: l.Sequence,
            Timestamp: l.Timestamp,
            Level: l.Level.ToString().ToLowerInvariant(),
            Stage: l.Stage,
            Text: l.Text)).ToList();
        return Result.Success<IReadOnlyList<DeployLogChunkDto>>(dtos);
    }
}
