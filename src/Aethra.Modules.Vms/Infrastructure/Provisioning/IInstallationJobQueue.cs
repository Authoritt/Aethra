using System.Threading.Channels;

namespace Aethra.Modules.Vms.Infrastructure.Provisioning;

/// <summary>
/// Job en la cola de installs SSH. Conserva las credenciales en plaintext SOLO en memoria,
/// nunca se persiste así — la persistencia cifrada vive en <c>Vm.SshCredentialsCipher</c>
/// (la guarda el handler antes de encolar).
/// </summary>
public sealed record InstallationJob(string VmId, InstallOptions Options,
    Aethra.Modules.Vms.Infrastructure.Security.SshCredentials Credentials);

/// <summary>
/// Cola in-process basada en <see cref="Channel{T}"/>. Single-reader, multi-writer:
/// el <see cref="InstallationDispatcher"/> consume uno a uno para evitar saturar la red SSH.
/// </summary>
public interface IInstallationJobQueue
{
    ValueTask EnqueueAsync(InstallationJob job, CancellationToken cancellationToken);
    IAsyncEnumerable<InstallationJob> ReadAllAsync(CancellationToken cancellationToken);
}

public sealed class InMemoryInstallationJobQueue : IInstallationJobQueue
{
    private readonly Channel<InstallationJob> _channel = Channel.CreateUnbounded<InstallationJob>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public ValueTask EnqueueAsync(InstallationJob job, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(job, cancellationToken);

    public IAsyncEnumerable<InstallationJob> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
