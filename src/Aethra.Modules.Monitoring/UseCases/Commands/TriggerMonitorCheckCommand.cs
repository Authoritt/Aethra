using Aethra.Modules.Monitoring.Domain;
using Aethra.Modules.Monitoring.Domain.Events;
using Aethra.Modules.Monitoring.Infrastructure;
using Aethra.Modules.Monitoring.Infrastructure.Probes;
using Aethra.Modules.Monitoring.UseCases.Dtos;
using Aethra.Shared.Contracts.Monitoring;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Infrastructure.Outbox;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Monitoring.UseCases.Commands;

/// <summary>
/// Fuerza un check ahora, sin esperar al siguiente tick del worker. Útil para validar configs
/// recién creadas. Reusa el mismo <see cref="IMonitorProbe"/> que el worker para garantizar
/// que el "trigger manual" mide exactamente lo que mide la observación continua.
/// </summary>
public sealed record TriggerMonitorCheckCommand(string MonitorId) : ICommand<MonitorCheckDto>;

internal sealed class TriggerMonitorCheckHandler(
    MonitoringDbContext db,
    IMonitorProbe probe,
    IOutboxWriter<MonitoringDbContext> outbox,
    IClock clock)
    : ICommandHandler<TriggerMonitorCheckCommand, MonitorCheckDto>
{
    public async Task<Result<MonitorCheckDto>> Handle(TriggerMonitorCheckCommand request, CancellationToken ct)
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

        var probeResult = await probe.ProbeAsync(monitor, ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        var check = MonitorCheck.Create(
            monitor.Id,
            now,
            probeResult.Status,
            probeResult.HttpStatusCode,
            probeResult.LatencyMs,
            probeResult.ErrorMessage,
            probeResult.ResponseSnippet);
        db.MonitorChecks.Add(check);
        monitor.RecordCheck(check);

        foreach (var ev in monitor.DomainEvents)
        {
            if (ev is MonitorStatusChangedEvent statusChange)
            {
                await outbox.EnqueueAsync(
                    new MonitorStatusChangedIntegrationEvent(
                        MonitorId: statusChange.MonitorId.ToString(),
                        From: statusChange.From.ToString(),
                        To: statusChange.To.ToString(),
                        CheckId: statusChange.CheckId.ToString(),
                        HttpStatusCode: probeResult.HttpStatusCode,
                        LatencyMs: probeResult.LatencyMs,
                        Timestamp: now),
                    ct).ConfigureAwait(false);
            }
        }
        monitor.ClearDomainEvents();

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return MonitorMapper.ToCheckDto(check);
    }
}
