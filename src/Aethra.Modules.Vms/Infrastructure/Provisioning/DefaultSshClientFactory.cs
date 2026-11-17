using Renci.SshNet;

namespace Aethra.Modules.Vms.Infrastructure.Provisioning;

/// <summary>
/// Implementación por defecto de <see cref="ISshClientFactory"/> basada en SSH.NET (Renci).
/// </summary>
public sealed class DefaultSshClientFactory : ISshClientFactory
{
    public ISshClient CreateClient(ConnectionInfo connectionInfo)
    {
        ArgumentNullException.ThrowIfNull(connectionInfo);
        return new RenciSshClientAdapter(new SshClient(connectionInfo));
    }
}

internal sealed class RenciSshClientAdapter : ISshClient
{
    private readonly SshClient _client;

    public RenciSshClientAdapter(SshClient client)
    {
        _client = client;
        _client.HostKeyReceived += (sender, e) =>
        {
            var args = new SshHostKeyReceivedEventArgs(
                hostKeyName: e.HostKeyName,
                hostKey: e.HostKey,
                keyLength: e.KeyLength,
                fingerPrintSHA256: e.FingerPrintSHA256);

            HostKeyReceived?.Invoke(this, args);
            e.CanTrust = args.CanTrust;
        };
    }

    public event EventHandler<SshHostKeyReceivedEventArgs>? HostKeyReceived;

    public ConnectionInfo ConnectionInfo => _client.ConnectionInfo;

    public bool IsConnected => _client.IsConnected;

    public Task ConnectAsync(CancellationToken cancellationToken)
        => _client.ConnectAsync(cancellationToken);

    public void Disconnect() => _client.Disconnect();

    public ISshCommand CreateCommand(string commandText)
        => new RenciSshCommandAdapter(_client.CreateCommand(commandText));

    public void Dispose() => _client.Dispose();
}

internal sealed class RenciSshCommandAdapter(SshCommand cmd) : ISshCommand
{
    public TimeSpan CommandTimeout
    {
        get => cmd.CommandTimeout;
        set => cmd.CommandTimeout = value;
    }

    public string Error => cmd.Error;

    public int? ExitStatus => cmd.ExitStatus;

    public string Execute() => cmd.Execute();

    public void CancelAsync() => cmd.CancelAsync();

    public void Dispose() => cmd.Dispose();
}
