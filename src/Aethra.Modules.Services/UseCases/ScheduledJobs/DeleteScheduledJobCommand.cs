using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Services.UseCases.ScheduledJobs;

public sealed record DeleteScheduledJobCommand(string JobId) : ICommand;

internal sealed class DeleteScheduledJobHandler(ServicesDbContext db)
    : ICommandHandler<DeleteScheduledJobCommand>
{
    public async Task<Result> Handle(DeleteScheduledJobCommand request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.JobId, out var parsed) || parsed.Value.Prefix != "sch")
        {
            return Error.Validation("scheduled_job.invalid_id", $"JobId invalido: '{request.JobId}'.");
        }
        var jid = new ScheduledJobId(parsed.Value);
        var job = await db.ScheduledJobs.FirstOrDefaultAsync(j => j.Id == jid, ct).ConfigureAwait(false);
        if (job is null)
        {
            return Error.NotFound("scheduled_job.not_found", $"Job '{request.JobId}' no existe.");
        }
        db.ScheduledJobs.Remove(job);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
