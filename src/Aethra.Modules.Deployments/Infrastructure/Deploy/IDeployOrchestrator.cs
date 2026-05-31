using Aethra.Modules.Deployments.Domain;

namespace Aethra.Modules.Deployments.Infrastructure.Deploy;

/// <summary>
/// Estrategia que ejecuta cada paso del deploy. Permite mock en tests y permite eventual split
/// local vs satélite (build local cuando target es la VM controladora, build remoto cuando target
/// es una VM secundaria).
/// </summary>
public interface IDeployOrchestrator
{
    Task RunAsync(DeployJobId jobId, CancellationToken ct);
}
