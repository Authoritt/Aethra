using System.Threading.Channels;
using Aethra.Modules.Deployments.Domain.Deployment;

namespace Aethra.Modules.Deployments.Infrastructure.Deployment;

/// <summary>
/// Cola in-process basada en <see cref="Channel{T}"/>. Single-reader / multi-writer:
/// el worker es uno solo (no procesamos deployments en paralelo en F9.3 para mantener
/// determinismo y limitar la presión sobre el satélite — un deployment a la vez por host).
/// </summary>
public sealed class InMemoryDeploymentJobQueue : IDeploymentJobQueue
{
    private readonly Channel<DeploymentId> _channel = Channel.CreateUnbounded<DeploymentId>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public ValueTask EnqueueAsync(DeploymentId deploymentId, CancellationToken ct)
        => _channel.Writer.WriteAsync(deploymentId, ct);

    public IAsyncEnumerable<DeploymentId> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
