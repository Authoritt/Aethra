using Aethra.Modules.Deployments.Domain.Deployment;

namespace Aethra.Modules.Deployments.Infrastructure.Deployment;

/// <summary>
/// Canal in-process del worker de deployments. La implementación concreta usa
/// <see cref="System.Threading.Channels.Channel{T}"/>.
///
/// <para>
/// Si Aethra se reinicia con deployments en cola, esos deployments siguen en BD con status
/// no terminal y un hosted service de recovery debería re-encolarlos al arrancar (F9.4).
/// </para>
/// </summary>
public interface IDeploymentJobQueue
{
    ValueTask EnqueueAsync(DeploymentId deploymentId, CancellationToken ct);
    IAsyncEnumerable<DeploymentId> ReadAllAsync(CancellationToken ct);
}
