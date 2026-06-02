namespace Aethra.Shared.Contracts.Containers;

/// <summary>
/// Estrategia de build que el satélite usa para materializar la imagen.
/// El central decide el modo a partir del <c>Template.BuildType</c> y lo envía al satélite
/// para que éste invoque al backend correcto (Docker.DotNet, podman CLI, nixpacks CLI, ...).
/// </summary>
public enum BuildMode
{
    /// <summary>Build clásico con Dockerfile. El satélite usa <c>docker build</c> / <c>podman build</c>.</summary>
    Dockerfile = 0,

    /// <summary>Build a partir de un <c>docker-compose.yml</c> (multi-service).</summary>
    DockerCompose = 1,

    /// <summary>Nixpacks: el satélite ejecuta <c>nixpacks build</c> sobre el contexto extraído.
    /// No requiere Dockerfile — autodetecta lenguaje (Node, Python, Go, Rust, Ruby, PHP, ...).</summary>
    Nixpacks = 2,
}

/// <summary>
/// Especificación agnóstica de runtime para construir una imagen de contenedor.
/// <para>
/// Se serializa via SignalR central → satélite. El satélite la pasa al
/// <c>IContainerRuntime</c> que decide cómo materializarla (Docker.DotNet vs CLI Podman
/// vs nixpacks CLI según <see cref="Mode"/>).
/// </para>
/// </summary>
/// <param name="ImageRef">Referencia completa de la imagen, p. ej. <c>localhost:5000/crm-saas/backend:abc1234</c>.</param>
/// <param name="BuildContextTarGz">Tarball gzip del workspace que se usará como contexto de build.</param>
/// <param name="DockerfilePath">Ruta relativa del Dockerfile dentro del tarball (sólo aplica si <see cref="Mode"/> == <see cref="BuildMode.Dockerfile"/>).</param>
/// <param name="BuildArgs">Pares clave/valor de argumentos de build (<c>--build-arg</c>). Soportado por todos los modos.</param>
/// <param name="BuildSecrets">Secretos opcionales tipo BuildKit (<c>--mount=type=secret</c>) si el runtime los soporta.</param>
/// <param name="Mode">Estrategia de build. Default <see cref="BuildMode.Dockerfile"/> para preservar compat con specs viejos.</param>
/// <param name="ComposeFilePath">Ruta al <c>docker-compose.yml</c> dentro del tarball (sólo aplica si <see cref="Mode"/> == <see cref="BuildMode.DockerCompose"/>).</param>
/// <param name="NixpacksConfig">Ruta opcional al <c>nixpacks.toml</c> dentro del tarball; <c>null</c> para auto-detect (sólo aplica si <see cref="Mode"/> == <see cref="BuildMode.Nixpacks"/>).</param>
public sealed record BuildSpec(
    string ImageRef,
    byte[] BuildContextTarGz,
    string DockerfilePath,
    IReadOnlyDictionary<string, string> BuildArgs,
    IReadOnlyDictionary<string, byte[]>? BuildSecrets,
    BuildMode Mode = BuildMode.Dockerfile,
    string? ComposeFilePath = null,
    string? NixpacksConfig = null);
