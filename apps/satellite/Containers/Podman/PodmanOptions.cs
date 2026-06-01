namespace Aethra.Satellite.Containers.Podman;

/// <summary>
/// Opciones configurables del runtime Podman. Se enlazan desde
/// la sección de configuración <c>Satellite:Podman</c>.
/// </summary>
public sealed class PodmanOptions
{
    /// <summary>
    /// Ruta al binario <c>podman</c>. Si es null o vacío, se asume que está en <c>$PATH</c>.
    /// </summary>
    public string? BinaryPath { get; set; }
}
