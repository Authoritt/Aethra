using Aethra.Modules.Vms.Infrastructure.Security;

namespace Aethra.Modules.Vms.Infrastructure.Provisioning;

/// <summary>
/// Opciones del install. <see cref="CentralUrl"/> es la URL pública del central (lo que el
/// script <c>curl</c>-ea para bajar el binario y luego usa el satélite como SignalR URL).
/// </summary>
public sealed record InstallOptions(
    string CentralUrl,
    string TokenPlaintext,
    string ContainerRuntime = "docker",
    bool InstallContainerRuntime = false);

/// <summary>Resultado de un intento de install. Si <see cref="Success"/> es false, <see cref="ErrorCode"/>
/// + <see cref="ErrorMessage"/> describen el problema legible para el frontend.</summary>
public sealed record InstallResult(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage,
    string Log);

/// <summary>
/// Provisioner SSH del satélite. Implementación concreta usa SSH.NET (Renci).
/// Cada paso emite progreso vía <see cref="IProgress{T}"/>.
/// </summary>
public interface ISshProvisioner
{
    /// <summary>
    /// Conecta por SSH, detecta arquitectura, opcionalmente instala Docker/Podman, descarga el
    /// binario del satélite desde el central, configura un systemd unit y arranca el servicio.
    /// Al terminar (si todo OK), verifica que el satélite haya hecho handshake via registry.
    /// </summary>
    Task<InstallResult> InstallSatelliteAsync(
        string vmId,
        SshCredentials credentials,
        InstallOptions options,
        IProgress<string> progress,
        CancellationToken cancellationToken);
}
