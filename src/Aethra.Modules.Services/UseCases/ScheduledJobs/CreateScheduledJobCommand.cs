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

public sealed record CreateScheduledJobCommand(
    string ServiceId,
    string Name,
    string? Description,
    string Command,
    string CronExpression,
    string? TimeZone,
    int? MaxConcurrent,
    int? TimeoutSeconds) : ICommand<ScheduledJobDto>;

internal sealed class CreateScheduledJobHandler(ServicesDbContext db, IClock clock)
    : ICommandHandler<CreateScheduledJobCommand, ScheduledJobDto>
{
    public async Task<Result<ScheduledJobDto>> Handle(CreateScheduledJobCommand request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.ServiceId, out var parsed) || parsed.Value.Prefix != "svc")
        {
            return Error.Validation("service.invalid_id", $"ServiceId invalido: '{request.ServiceId}'.");
        }
        var sid = new ManagedServiceId(parsed.Value);
        var exists = await db.ManagedServices.AnyAsync(s => s.Id == sid, ct).ConfigureAwait(false);
        if (!exists)
        {
            return Error.NotFound("service.not_found", $"Servicio '{request.ServiceId}' no existe.");
        }
        if (!CronExpression.TryParse(request.CronExpression, out _))
        {
            return Error.Validation("scheduled_job.invalid_cron",
                $"CronExpression invalida: '{request.CronExpression}'. Formato: 'minute hour day month dow' (ej. '0 2 * * *').");
        }

        ScheduledJob job;
        try
        {
            job = ScheduledJob.Create(
                sid, request.Name, request.Description, request.Command,
                request.CronExpression, request.TimeZone,
                request.MaxConcurrent, request.TimeoutSeconds, clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("scheduled_job.invalid", ex.Message);
        }

        db.ScheduledJobs.Add(job);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(job);
    }

    internal static ScheduledJobDto Map(ScheduledJob j) => new(
        j.Id.ToString(), j.ServiceId.ToString(), j.Name, j.Description,
        j.Command, j.CronExpression, j.TimeZone, j.Enabled,
        j.MaxConcurrent, j.TimeoutSeconds, j.LastRunAt, j.NextRunAt,
        j.CreatedAt, j.UpdatedAt);
}
