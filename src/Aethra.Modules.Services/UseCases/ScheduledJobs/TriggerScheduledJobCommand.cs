using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.Infrastructure.Scheduling;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Services.UseCases.ScheduledJobs;

public sealed record TriggerScheduledJobCommand(string JobId) : ICommand<string>;

internal sealed class TriggerScheduledJobHandler(
    ServicesDbContext db,
    ScheduledJobWorker worker)
    : ICommandHandler<TriggerScheduledJobCommand, string>
{
    public async Task<Result<string>> Handle(TriggerScheduledJobCommand request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.JobId, out var parsed) || parsed.Value.Prefix != "sch")
        {
            return Error.Validation("scheduled_job.invalid_id", $"JobId invalido: '{request.JobId}'.");
        }
        var jid = new ScheduledJobId(parsed.Value);
        var job = await db.ScheduledJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jid, ct)
            .ConfigureAwait(false);
        if (job is null)
        {
            return Error.NotFound("scheduled_job.not_found", $"Job '{request.JobId}' no existe.");
        }
        var runId = await worker.TriggerNowAsync(jid, ct).ConfigureAwait(false);
        if (runId is null)
        {
            return Error.Failure("scheduled_job.trigger_failed", "No se pudo crear el run.");
        }
        return runId.Value.ToString();
    }
}
