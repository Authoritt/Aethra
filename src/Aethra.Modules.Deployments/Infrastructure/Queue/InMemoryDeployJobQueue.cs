using System.Threading.Channels;
using Aethra.Modules.Deployments.Domain;
using Aethra.Modules.Deployments.UseCases.Commands;

namespace Aethra.Modules.Deployments.Infrastructure.Queue;

/// <summary>
/// Cola in-process basada en <see cref="Channel{T}"/>. Single-writer-multiple-reader pero
/// el worker es uno solo (no procesamos deploys en paralelo en F4 para mantener simplicidad).
///
/// Si Aethra se reinicia con jobs en cola, esos jobs siguen en BD con status=Queued y un
/// hosted service de recovery los re-encolará al arrancar (ver <c>DeployJobRecoveryHost</c>).
/// </summary>
public sealed class InMemoryDeployJobQueue : IDeployJobQueue
{
    private readonly Channel<DeployJobId> _channel = Channel.CreateUnbounded<DeployJobId>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public ValueTask EnqueueAsync(DeployJobId jobId, CancellationToken ct)
        => _channel.Writer.WriteAsync(jobId, ct);

    public IAsyncEnumerable<DeployJobId> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
