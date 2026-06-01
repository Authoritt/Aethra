using Aethra.Modules.Deployments.Domain.Deployment;

namespace Aethra.Modules.Deployments.Infrastructure.Deployment;

/// <summary>
/// Orquesta el ciclo de vida de un <see cref="Deployment"/>: pull → run → healthcheck → swap.
/// La implementación concreta (<see cref="DeploymentOrchestrator"/>) avanza la state machine,
/// publica logs y emite el evento de integración final (atomic swap YARP en F9.4).
/// </summary>
public interface IDeploymentOrchestrator
{
    /// <summary>
    /// Ejecuta el pipeline completo del deployment identificado. Cualquier excepción no esperada
    /// se atrapa internamente y se traduce a un <see cref="DeploymentStatus.Failed"/> con código
    /// <c>internal_error</c>; no propaga al worker para no romper el loop.
    /// </summary>
    Task RunAsync(DeploymentId deploymentId, CancellationToken ct);
}
