using Aethra.Modules.Deployments.Domain.Build;

namespace Aethra.Modules.Deployments.Infrastructure.Build;

/// <summary>
/// Orquesta el ciclo de vida de un <see cref="Build"/>: clonar → buildear → pushear.
/// La implementación concreta (<see cref="BuildOrchestrator"/>) avanza la state machine,
/// publica logs y emite el evento de integración final.
/// </summary>
public interface IBuildOrchestrator
{
    /// <summary>
    /// Ejecuta el pipeline completo del build identificado. Cualquier excepción no esperada
    /// se atrapa internamente y se traduce a un <see cref="BuildStatus.Failed"/> con código
    /// <c>internal_error</c>; no propaga al worker para no romper el loop.
    /// </summary>
    Task RunAsync(BuildId buildId, CancellationToken ct);
}
