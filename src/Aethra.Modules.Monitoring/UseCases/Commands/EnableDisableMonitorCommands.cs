using Aethra.Modules.Monitoring.Domain;
using Aethra.Modules.Monitoring.Infrastructure;
using Aethra.Modules.Monitoring.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Monitoring.UseCases.Commands;

public sealed record EnableMonitorCommand(string MonitorId) : ICommand<MonitorDetailDto>;

internal sealed class EnableMonitorHandler(MonitoringDbContext db, IClock clock)
    : ICommandHandler<EnableMonitorCommand, MonitorDetailDto>
{
    public async Task<Result<MonitorDetailDto>> Handle(EnableMonitorCommand request, CancellationToken ct)
    {
        var found = await FindAsync(db, request.MonitorId, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }
        found.Value.Enable(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return MonitorMapper.ToDetail(found.Value);
    }

    internal static async Task<Result<Monitor>> FindAsync(MonitoringDbContext db, string id, CancellationToken ct)
    {
        if (!AethraId.TryParse(id, out var parsed) || parsed.Value.Prefix != "mon")
        {
            return Error.Validation("monitor.invalid_id", "ID de monitor inválido.");
        }
        var typedId = new MonitorId(parsed.Value);
        var monitor = await db.Monitors.FirstOrDefaultAsync(m => m.Id == typedId, ct).ConfigureAwait(false);
        if (monitor is null)
        {
            return Error.NotFound("monitor.not_found", $"Monitor '{id}' no existe.");
        }
        return monitor;
    }
}

public sealed record DisableMonitorCommand(string MonitorId) : ICommand<MonitorDetailDto>;

internal sealed class DisableMonitorHandler(MonitoringDbContext db, IClock clock)
    : ICommandHandler<DisableMonitorCommand, MonitorDetailDto>
{
    public async Task<Result<MonitorDetailDto>> Handle(DisableMonitorCommand request, CancellationToken ct)
    {
        var found = await EnableMonitorHandler.FindAsync(db, request.MonitorId, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }
        found.Value.Disable(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return MonitorMapper.ToDetail(found.Value);
    }
}
