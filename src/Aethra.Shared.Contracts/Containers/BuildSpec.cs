namespace Aethra.Shared.Contracts.Containers;

/// <summary>
/// Especificación agnóstica de runtime para construir una imagen de contenedor.
/// <para>
/// Se serializa via SignalR central → satélite. El satélite la pasa al
/// <c>IContainerRuntime</c> que decide cómo materializarla (Docker.DotNet vs CLI Podman).
/// </para>
/// </summary>
/// <param name="ImageRef">Referencia completa de la imagen, p. ej. <c>localhost:5000/crm-saas/backend:abc1234</c>.</param>
/// <param name="BuildContextTarGz">Tarball gzip del workspace que se usará como contexto de build.</param>
/// <param name="DockerfilePath">Ruta relativa del Dockerfile dentro del tarball.</param>
/// <param name="BuildArgs">Pares clave/valor de argumentos de build (<c>--build-arg</c>).</param>
/// <param name="BuildSecrets">Secretos opcionales tipo BuildKit (<c>--mount=type=secret</c>) si el runtime los soporta.</param>
public sealed record BuildSpec(
    string ImageRef,
    byte[] BuildContextTarGz,
    string DockerfilePath,
    IReadOnlyDictionary<string, string> BuildArgs,
    IReadOnlyDictionary<string, byte[]>? BuildSecrets);
