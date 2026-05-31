using Aethra.Modules.Deployments.Domain;
using Aethra.Modules.Deployments.Infrastructure;
using Aethra.Modules.Deployments.UseCases.Dtos;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Deployments.UseCases.Queries;

public sealed record ListDeploysQuery(string ApplicationId, int Limit = 50) : IQuery<IReadOnlyList<DeployJobSummaryDto>>;

internal sealed class ListDeploysHandler(DeploymentsDbContext db, IApplicationLookup lookup)
    : IQueryHandler<ListDeploysQuery, IReadOnlyList<DeployJobSummaryDto>>
{
    public async Task<Result<IReadOnlyList<DeployJobSummaryDto>>> Handle(ListDeploysQuery request, CancellationToken ct)
    {
        var limit = Math.Clamp(request.Limit, 1, 200);
        var jobs = await db.DeployJobs
            .AsNoTracking()
            .Where(j => j.ApplicationId == request.ApplicationId)
            .OrderByDescending(j => j.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        var app = await lookup.GetByIdAsync(request.ApplicationId, ct);
        var slug = app?.Slug ?? "unknown";

        var dtos = jobs.Select(j => MapToDto(j, slug)).ToList();
        return Result.Success<IReadOnlyList<DeployJobSummaryDto>>(dtos);
    }

    internal static DeployJobSummaryDto MapToDto(DeployJob j, string slug)
    {
        var duration = j is { StartedAt: { } s, FinishedAt: { } f } ? (f - s).TotalSeconds : (double?)null;
        return new DeployJobSummaryDto(
            Id: j.Id.ToString(),
            ApplicationId: j.ApplicationId,
            ApplicationSlug: slug,
            GitSha: j.GitSha,
            ShortSha: j.GitSha.Length >= 7 ? j.GitSha[..7] : j.GitSha,
            Status: j.Status.ToString().ToLowerInvariant(),
            Trigger: j.Trigger.ToString().ToLowerInvariant(),
            TriggeredBy: j.TriggeredBy,
            CreatedAt: j.CreatedAt,
            StartedAt: j.StartedAt,
            FinishedAt: j.FinishedAt,
            DurationSeconds: duration,
            ErrorCode: j.ErrorCode,
            ErrorMessage: j.ErrorMessage);
    }
}
