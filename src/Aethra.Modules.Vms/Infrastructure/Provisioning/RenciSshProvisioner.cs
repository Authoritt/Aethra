using System.Net.Sockets;
using Aethra.Modules.Vms.Infrastructure.Security;
using Aethra.Shared.Contracts.Containers;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Aethra.Modules.Vms.Infrastructure.Provisioning;

/// <summary>
/// Implementación de <see cref="ISshProvisioner"/> usando SSH.NET (Renci).
///
/// Flow:
/// <list type="number">
/// <item>Conectar via SSH con timeout corto (30s).</item>
/// <item>Detectar arquitectura (<c>uname -m</c> → linux-x64 / linux-arm64).</item>
/// <item>Detectar Docker/Podman; si falta y <c>InstallContainerRuntime</c>, instalarlo
/// (apt-get o dnf según distro).</item>
/// <item><c>curl</c> al endpoint <c>/api/satellite/binary?arch=...</c> del central.</item>
/// <item>Extraer en <c>/opt/aethra-satellite</c>, escribir un systemd unit, arrancar.</item>
/// <item>Polling hasta 60s al <see cref="ISatelliteConnectionRegistry"/> para verificar
/// que el satélite hizo handshake.</item>
/// </list>
/// </summary>
public sealed class RenciSshProvisioner(
    ISatelliteConnectionRegistry registry,
    ILogger<RenciSshProvisioner> logger) : ISshProvisioner
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan VerifyHandshakeTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan VerifyPollInterval = TimeSpan.FromSeconds(2);

    public async Task<InstallResult> InstallSatelliteAsync(
        string vmId,
        SshCredentials credentials,
        InstallOptions options,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vmId);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(progress);

        var validation = credentials.Validate();
        if (validation is not null)
        {
            return Fail(validation, $"Credenciales SSH inválidas: {validation}", progress);
        }

        SshClient? client = null;
        try
        {
            progress.Report($"Conectando a {credentials.User}@{credentials.Host}:{credentials.Port}…");
            var connectionInfo = BuildConnectionInfo(credentials);
            client = new SshClient(connectionInfo);

            try
            {
                await client.ConnectAsync(cancellationToken);
            }
            catch (SshAuthenticationException ex)
            {
                return Fail("ssh_auth_failed", $"Autenticación SSH falló: {ex.Message}", progress);
            }
            catch (SshConnectionException ex)
            {
                return Fail("ssh_connect_failed", $"No se pudo conectar al host SSH: {ex.Message}", progress);
            }
            catch (SocketException ex)
            {
                return Fail("ssh_connect_failed", $"Socket error al conectar SSH: {ex.Message}", progress);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Fail("ssh_connect_failed", $"Error conectando SSH: {ex.Message}", progress);
            }

            progress.Report("SSH conectado.");

            // 1. Detectar arch
            var (archOk, archResult) = await RunAsync(client, "uname -m", progress, cancellationToken);
            if (!archOk)
            {
                return Fail("ssh_command_failed", $"uname -m falló: {archResult}", progress);
            }
            var arch = archResult.Trim();
            var binArch = arch switch
            {
                "x86_64" => "linux-x64",
                "aarch64" => "linux-arm64",
                _ => null,
            };
            if (binArch is null)
            {
                return Fail("unsupported_arch", $"Arquitectura no soportada: {arch}", progress);
            }
            progress.Report($"Arquitectura detectada: {arch} → {binArch}");

            // 2. Detectar / instalar runtime
            var runtime = string.IsNullOrWhiteSpace(options.ContainerRuntime) ? "docker" : options.ContainerRuntime;
            var runtimeCheck = await ExecuteAsync(client, $"command -v {runtime} >/dev/null 2>&1 && echo OK || echo MISSING",
                cancellationToken);
            var runtimePresent = runtimeCheck.StdOut.Contains("OK", StringComparison.Ordinal);
            if (!runtimePresent && options.InstallContainerRuntime)
            {
                progress.Report($"{runtime} no encontrado. Intentando instalar…");
                var pkgCmd = BuildInstallRuntimeCommand(runtime);
                var (instOk, instLog) = await RunAsync(client, pkgCmd, progress, cancellationToken);
                if (!instOk)
                {
                    return Fail("runtime_install_failed",
                        $"No se pudo instalar {runtime}: {instLog}", progress);
                }
                progress.Report($"{runtime} instalado.");
            }
            else if (!runtimePresent)
            {
                progress.Report($"Aviso: {runtime} no está instalado y --install-runtime no fue solicitado.");
            }

            // 3. Descargar binario
            var downloadUrl = $"{options.CentralUrl.TrimEnd('/')}/api/satellite/binary?arch={binArch}";
            progress.Report($"Descargando satélite desde {downloadUrl}…");
            var (dlOk, dlLog) = await RunAsync(client,
                $"sudo -n curl -fL -o /tmp/aethra-sat.tar.gz '{downloadUrl}'",
                progress, cancellationToken);
            if (!dlOk)
            {
                return Fail("binary_download_failed", $"Curl falló: {dlLog}", progress);
            }

            // 4. Extraer + systemd unit
            progress.Report("Extrayendo binario en /opt/aethra-satellite…");
            var (extOk, extLog) = await RunAsync(client,
                "sudo -n mkdir -p /opt/aethra-satellite && sudo -n tar -xzf /tmp/aethra-sat.tar.gz -C /opt/aethra-satellite && sudo -n chmod +x /opt/aethra-satellite/Aethra.Satellite",
                progress, cancellationToken);
            if (!extOk)
            {
                return Fail("extract_failed", $"tar/chmod falló: {extLog}", progress);
            }

            progress.Report("Escribiendo systemd unit en /etc/systemd/system/aethra-satellite.service…");
            var unit = BuildSystemdUnit(options);
            // tee escribe via stdin. Hacemos un base64 encode para evitar problemas de escape.
            var unitB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(unit));
            var (unitOk, unitLog) = await RunAsync(client,
                $"echo '{unitB64}' | base64 -d | sudo -n tee /etc/systemd/system/aethra-satellite.service >/dev/null",
                progress, cancellationToken);
            if (!unitOk)
            {
                return Fail("systemd_write_failed", $"Escribir unit falló: {unitLog}", progress);
            }

            progress.Report("systemctl daemon-reload && enable --now aethra-satellite…");
            var (svcOk, svcLog) = await RunAsync(client,
                "sudo -n systemctl daemon-reload && sudo -n systemctl enable --now aethra-satellite",
                progress, cancellationToken);
            if (!svcOk)
            {
                return Fail("systemd_start_failed", $"systemctl falló: {svcLog}", progress);
            }

            progress.Report("Servicio arrancado. Esperando handshake del satélite…");

            // 5. Verificar handshake
            var verified = await WaitForHandshakeAsync(vmId, progress, cancellationToken);
            if (!verified)
            {
                // Intentamos capturar journalctl para diagnóstico.
                var journal = await ExecuteAsync(client,
                    "sudo -n journalctl -u aethra-satellite --no-pager -n 40 2>/dev/null || true",
                    cancellationToken);
                progress.Report("Últimas líneas de journalctl (debug):");
                foreach (var line in journal.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    progress.Report(line);
                }
                return Fail("handshake_timeout",
                    "El satélite arrancó pero no se conectó al central en 60s.", progress);
            }

            progress.Report("Satélite conectado y registrado. Instalación completa.");
            return new InstallResult(Success: true, ErrorCode: null, ErrorMessage: null, Log: string.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Provisioner SSH falló inesperadamente para VM {VmId}", vmId);
            return Fail("provisioner_unexpected", $"Error inesperado: {ex.Message}", progress);
        }
        finally
        {
            try
            {
                client?.Disconnect();
            }
            catch { /* swallow */ }
            client?.Dispose();
        }
    }

    private static ConnectionInfo BuildConnectionInfo(SshCredentials creds)
    {
        AuthenticationMethod auth = creds.AuthMethod switch
        {
            SshAuthMethod.Password => new PasswordAuthenticationMethod(creds.User, creds.Value),
            SshAuthMethod.Key => BuildKeyAuth(creds),
            _ => throw new InvalidOperationException($"Auth method no soportado: {creds.AuthMethod}"),
        };
        return new ConnectionInfo(creds.Host, creds.Port, creds.User, auth)
        {
            Timeout = ConnectTimeout,
        };
    }

    private static PrivateKeyAuthenticationMethod BuildKeyAuth(SshCredentials creds)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(creds.Value));
        var keyFile = new PrivateKeyFile(stream);
        return new PrivateKeyAuthenticationMethod(creds.User, keyFile);
    }

    private async Task<(bool Success, string Log)> RunAsync(SshClient client, string command,
        IProgress<string> progress, CancellationToken cancellationToken)
    {
        progress.Report($"$ {Trim(command)}");
        var (stdout, stderr, exit) = await ExecuteRawAsync(client, command, cancellationToken);
        if (!string.IsNullOrWhiteSpace(stdout))
        {
            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                progress.Report(line);
            }
        }
        if (exit != 0)
        {
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                foreach (var line in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    progress.Report($"stderr: {line}");
                }
            }
            return (false, string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
        }
        return (true, stdout);
    }

    private static async Task<(string StdOut, string StdErr, int ExitCode)> ExecuteRawAsync(
        SshClient client, string command, CancellationToken cancellationToken)
    {
        using var cmd = client.CreateCommand(command);
        cmd.CommandTimeout = CommandTimeout;
        await using (cancellationToken.Register(() =>
        {
            try { cmd.CancelAsync(); } catch { /* swallow */ }
        }))
        {
            var result = await Task.Run(() => cmd.Execute(), cancellationToken);
            return (result ?? string.Empty, cmd.Error ?? string.Empty, cmd.ExitStatus ?? -1);
        }
    }

    private static async Task<(string StdOut, string StdErr, int ExitCode)> ExecuteAsync(
        SshClient client, string command, CancellationToken cancellationToken)
        => await ExecuteRawAsync(client, command, cancellationToken);

    private async Task<bool> WaitForHandshakeAsync(string vmId, IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + VerifyHandshakeTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (registry.IsConnected(vmId))
            {
                return true;
            }
            progress.Report("Esperando handshake…");
            try
            {
                await Task.Delay(VerifyPollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
        return false;
    }

    private static string BuildInstallRuntimeCommand(string runtime)
    {
        // Detect package manager y arma el comando inline. Soportamos:
        // - apt (Debian/Ubuntu) → docker.io o podman
        // - dnf (RHEL/Fedora) → docker / podman
        var pkg = runtime == "podman" ? "podman" : "docker.io";
        var pkgDnf = runtime == "podman" ? "podman" : "docker";
        // Una sola línea bash con fallback.
        return $"if command -v apt-get >/dev/null 2>&1; then " +
               $"  sudo -n apt-get update && sudo -n DEBIAN_FRONTEND=noninteractive apt-get install -y {pkg}; " +
               $"elif command -v dnf >/dev/null 2>&1; then " +
               $"  sudo -n dnf install -y {pkgDnf}; " +
               $"else echo \"No package manager soportado (apt-get/dnf).\" >&2 && exit 1; fi && " +
               $"sudo -n systemctl enable --now {runtime}";
    }

    private static string BuildSystemdUnit(InstallOptions opts)
    {
        // Heredoc-style. Escapamos las comillas con interpolation.
        return $"""
            [Unit]
            Description=Aethra Satellite
            After=network-online.target
            Wants=network-online.target

            [Service]
            Type=simple
            ExecStart=/opt/aethra-satellite/Aethra.Satellite
            Environment=AETHRA_CENTRAL_URL={opts.CentralUrl}
            Environment=AETHRA_SATELLITE_TOKEN={opts.TokenPlaintext}
            Environment=Satellite__ContainerRuntime={opts.ContainerRuntime}
            Restart=always
            RestartSec=5

            [Install]
            WantedBy=multi-user.target

            """;
    }

    private static InstallResult Fail(string code, string message, IProgress<string> progress)
    {
        progress.Report($"FAILED: {code} — {message}");
        return new InstallResult(false, code, message, string.Empty);
    }

    private static string Trim(string cmd) => cmd.Length > 240 ? cmd[..240] + "…" : cmd;
}
