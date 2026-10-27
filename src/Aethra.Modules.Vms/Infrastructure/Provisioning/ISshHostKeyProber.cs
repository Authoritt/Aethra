using System.Net.Sockets;
using Aethra.Modules.Vms.Domain;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Aethra.Modules.Vms.Infrastructure.Provisioning;

public sealed record SshHostKeyProbeResult(
    bool Success,
    string? HostKeyType,
    string? Fingerprint,
    string? ErrorCode,
    string? ErrorMessage);

public interface ISshHostKeyProber
{
    Task<SshHostKeyProbeResult> ProbeAsync(string host, int port, CancellationToken ct);
}

public sealed class RenciSshHostKeyProber(
    ISshClientFactory clientFactory,
    ILogger<RenciSshHostKeyProber> logger) : ISshHostKeyProber
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    public async Task<SshHostKeyProbeResult> ProbeAsync(string host, int port, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port <= 0 || port > 65535)
        {
            return new SshHostKeyProbeResult(false, null, null, "ssh.port_invalid", "Puerto SSH inválido.");
        }

        string? receivedKeyType = null;
        string? receivedFingerprint = null;

        var connectionInfo = new ConnectionInfo(host, port, "probe", new NoneAuthenticationMethod("probe"))
        {
            Timeout = ProbeTimeout,
        };

        ISshClient? client = null;
        try
        {
            client = clientFactory.CreateClient(connectionInfo);
            client.HostKeyReceived += (sender, e) =>
            {
                receivedKeyType = e.HostKeyName;
                receivedFingerprint = SshHostKeyHelper.FormatFingerprint(e.HostKey);
                // No continuamos a autenticación: solo queríamos la clave pública del host
                e.CanTrust = false;
            };

            try
            {
                await client.ConnectAsync(ct);
            }
            catch (SshConnectionException)
            {
                // Esperado si CanTrust=false aborta tras capturar la host key
            }
            catch (SshAuthenticationException)
            {
                // Esperado si el servidor requiere auth tras recibir la clave
            }
            catch (SocketException ex)
            {
                return new SshHostKeyProbeResult(false, null, null, "ssh_connect_failed", $"Socket error: {ex.Message}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException && receivedFingerprint is null)
            {
                return new SshHostKeyProbeResult(false, null, null, "ssh_connect_failed", $"Error conectando al host SSH: {ex.Message}");
            }

            if (receivedFingerprint is not null)
            {
                return new SshHostKeyProbeResult(true, receivedKeyType, receivedFingerprint, null, null);
            }

            return new SshHostKeyProbeResult(false, null, null, "ssh_no_host_key", "El servidor no presentó ninguna clave pública de host.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error inesperado al sondear host SSH {Host}:{Port}", host, port);
            return new SshHostKeyProbeResult(false, null, null, "ssh_probe_failed", ex.Message);
        }
        finally
        {
            try { client?.Disconnect(); } catch { /* swallow */ }
            client?.Dispose();
        }
    }
}
