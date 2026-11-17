using Aethra.Modules.Vms.Domain;
using Renci.SshNet;

namespace Aethra.Modules.Vms.Infrastructure.Provisioning;

/// <summary>
/// Evento emitido al recibir la clave pública del host SSH durante el intercambio de claves (KEX).
/// </summary>
public sealed class SshHostKeyReceivedEventArgs(
    string hostKeyName,
    byte[] hostKey,
    int keyLength,
    string? fingerPrintSHA256 = null) : EventArgs
{
    public string HostKeyName { get; } = hostKeyName;
    public byte[] HostKey { get; } = hostKey;
    public int KeyLength { get; } = keyLength;
    public string FingerPrintSHA256 { get; } = fingerPrintSHA256 ?? SshHostKeyHelper.FormatFingerprint(hostKey);

    /// <summary>
    /// Si es false, la conexión se aborta inmediatamente antes de enviar credenciales o comandos.
    /// </summary>
    public bool CanTrust { get; set; }
}

/// <summary>
/// Abstracción para comandos remotos SSH.
/// </summary>
public interface ISshCommand : IDisposable
{
    TimeSpan CommandTimeout { get; set; }
    string Error { get; }
    int? ExitStatus { get; }
    string Execute();
    void CancelAsync();
}

/// <summary>
/// Abstracción del cliente SSH para permitir pruebas completas sin red real.
/// </summary>
public interface ISshClient : IDisposable
{
    event EventHandler<SshHostKeyReceivedEventArgs>? HostKeyReceived;
    ConnectionInfo ConnectionInfo { get; }
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken cancellationToken);
    void Disconnect();
    ISshCommand CreateCommand(string commandText);
}

/// <summary>
/// Factoría para crear clientes SSH según ConnectionInfo.
/// </summary>
public interface ISshClientFactory
{
    ISshClient CreateClient(ConnectionInfo connectionInfo);
}
