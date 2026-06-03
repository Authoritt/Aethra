using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Services.UseCases.ScheduledJobs;

public sealed record ListScheduledJobsQuery(string ServiceId) : IQuery<IReadOnlyList<ScheduledJobDto>>;

internal sealed class ListScheduledJobsHandler(ServicesDbContext db)
    : IQueryHandler<ListScheduledJobsQuery, IReadOnlyList<ScheduledJobDto>>
{
    public async Task<Result<IReadOnlyList<ScheduledJobDto>>> Handle(ListScheduledJobsQuery request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.ServiceId, out var parsed) || parsed.Value.Prefix != "svc")
        {
            return Error.Validation("service.invalid_id", $"ServiceId invalido: '{request.ServiceId}'.");
        }
        var sid = new ManagedServiceId(parsed.Value);
        var rows = await db.ScheduledJobs.AsNoTracking()
            .Where(j => j.ServiceId == sid)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        IReadOnlyList<ScheduledJobDto> result = rows.Select(CreateScheduledJobHandler.Map).ToList();
        return Result<IReadOnlyList<ScheduledJobDto>>.Success(result);
    }
}
