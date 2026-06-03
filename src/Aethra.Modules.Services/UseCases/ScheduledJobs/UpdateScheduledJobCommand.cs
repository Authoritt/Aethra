using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.Infrastructure.Scheduling;
using Aethra.Modules.Services.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Services.UseCases.ScheduledJobs;

public sealed record UpdateScheduledJobCommand(
    string JobId,
    string? Name,
    string? Description,
    string? Command,
    string? CronExpression,
    string? TimeZone,
    int? MaxConcurrent,
    int? TimeoutSeconds,
    bool? Enabled) : ICommand<ScheduledJobDto>;

internal sealed class UpdateScheduledJobHandler(ServicesDbContext db, IClock clock)
    : ICommandHandler<UpdateScheduledJobCommand, ScheduledJobDto>
{
    public async Task<Result<ScheduledJobDto>> Handle(UpdateScheduledJobCommand request, CancellationToken ct)
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

        if (request.CronExpression is { } cron && !CronExpression.TryParse(cron, out _))
        {
            return Error.Validation("scheduled_job.invalid_cron", $"CronExpression invalida: '{cron}'.");
        }

        try
        {
            job.UpdateDefinition(request.Name, request.Description, request.Command,
                request.CronExpression, request.TimeZone, request.MaxConcurrent,
                request.TimeoutSeconds, clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("scheduled_job.invalid", ex.Message);
        }

        if (request.Enabled is { } en)
        {
            job.SetEnabled(en, clock.UtcNow);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return CreateScheduledJobHandler.Map(job);
    }
}
