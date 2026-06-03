using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Services.UseCases.ScheduledJobs;

public sealed record ListScheduledJobRunsQuery(string JobId, int Limit)
    : IQuery<IReadOnlyList<ScheduledJobRunDto>>;

internal sealed class ListScheduledJobRunsHandler(ServicesDbContext db)
    : IQueryHandler<ListScheduledJobRunsQuery, IReadOnlyList<ScheduledJobRunDto>>
{
    public async Task<Result<IReadOnlyList<ScheduledJobRunDto>>> Handle(
        ListScheduledJobRunsQuery request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.JobId, out var parsed) || parsed.Value.Prefix != "sch")
        {
            return Error.Validation("scheduled_job.invalid_id", $"JobId invalido: '{request.JobId}'.");
        }
        var jid = new ScheduledJobId(parsed.Value);
        var limit = Math.Clamp(request.Limit, 1, 500);
        var rows = await db.ScheduledJobRuns.AsNoTracking()
            .Where(r => r.JobId == jid)
            .OrderByDescending(r => r.StartedAt)
            .Take(limit)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        IReadOnlyList<ScheduledJobRunDto> result = rows.Select(r => new ScheduledJobRunDto(
            r.Id.ToString(), r.JobId.ToString(), r.StartedAt, r.FinishedAt,
            r.Status.ToString(), r.ExitCode, r.Stdout, r.Stderr, r.DurationMs))
            .ToList();

        return Result<IReadOnlyList<ScheduledJobRunDto>>.Success(result);
    }
}
