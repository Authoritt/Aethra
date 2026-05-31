using Aethra.Modules.Monitoring.Domain;

namespace Aethra.Modules.Monitoring.Infrastructure.Probes;

/// <summary>
/// Resultado de un probe HTTP contra un <see cref="Monitor"/>. Inmutable, sin side-effects.
/// El llamador (worker) decide qué hacer: persistir, emitir eventos, etc.
/// </summary>
public sealed record MonitorProbeResult(
    MonitorStatus Status,
    int? HttpStatusCode,
    int? LatencyMs,
    string? ErrorMessage,
    string? ResponseSnippet);

/// <summary>
/// Abstracción que ejecuta una sonda HTTP contra un monitor. Implementación real:
/// <see cref="HttpMonitorProbe"/>. En tests se puede sustituir por un fake.
/// </summary>
public interface IMonitorProbe
{
    Task<MonitorProbeResult> ProbeAsync(Monitor monitor, CancellationToken ct);
}
