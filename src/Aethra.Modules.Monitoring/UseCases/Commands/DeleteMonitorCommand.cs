using Aethra.Modules.Monitoring.Domain;
using Aethra.Modules.Monitoring.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Monitoring.UseCases.Commands;

public sealed record DeleteMonitorCommand(string MonitorId) : ICommand;

internal sealed class DeleteMonitorHandler(MonitoringDbContext db) : ICommandHandler<DeleteMonitorCommand>
{
    public async Task<Result> Handle(DeleteMonitorCommand request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.MonitorId, out var parsed) || parsed.Value.Prefix != "mon")
        {
            return Error.Validation("monitor.invalid_id", "ID de monitor inválido.");
        }
        var typedId = new MonitorId(parsed.Value);

        var monitor = await db.Monitors.FirstOrDefaultAsync(m => m.Id == typedId, ct).ConfigureAwait(false);
        if (monitor is null)
        {
            return Error.NotFound("monitor.not_found", $"Monitor '{request.MonitorId}' no existe.");
        }

        // Borra checks asociados — F6 no necesita historiar el motivo de borrado.
        var checks = await db.MonitorChecks.Where(c => c.MonitorId == typedId).ToListAsync(ct).ConfigureAwait(false);
        db.MonitorChecks.RemoveRange(checks);
        db.Monitors.Remove(monitor);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
