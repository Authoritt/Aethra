using System.Threading.Channels;
using Aethra.Modules.Deployments.Domain.Build;

namespace Aethra.Modules.Deployments.Infrastructure.Build;

/// <summary>
/// Cola in-process basada en <see cref="Channel{T}"/>. Single-reader / multi-writer:
/// el worker es uno solo (no procesamos builds en paralelo en F9.3 — un build a la vez
/// mantiene determinismo y limita la presión sobre el satélite).
/// </summary>
public sealed class InMemoryBuildJobQueue : IBuildJobQueue
{
    private readonly Channel<BuildId> _channel = Channel.CreateUnbounded<BuildId>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public ValueTask EnqueueAsync(BuildId buildId, CancellationToken ct)
        => _channel.Writer.WriteAsync(buildId, ct);

    public IAsyncEnumerable<BuildId> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
