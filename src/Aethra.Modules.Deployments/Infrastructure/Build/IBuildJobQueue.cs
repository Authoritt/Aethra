using Aethra.Modules.Deployments.Domain.Build;

namespace Aethra.Modules.Deployments.Infrastructure.Build;

/// <summary>
/// Canal in-process del worker de builds. La implementación concreta usa
/// <see cref="System.Threading.Channels.Channel{T}"/>.
///
/// Si Aethra se reinicia con builds en cola, esos builds siguen en BD con status no terminal
/// y un hosted service de recovery debería re-encolarlos al arrancar (F9.3.5).
/// </summary>
public interface IBuildJobQueue
{
    ValueTask EnqueueAsync(BuildId buildId, CancellationToken ct);
    IAsyncEnumerable<BuildId> ReadAllAsync(CancellationToken ct);
}
