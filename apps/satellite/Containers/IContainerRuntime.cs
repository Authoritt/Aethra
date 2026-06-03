using Aethra.Shared.Contracts.Containers;

namespace Aethra.Satellite.Containers;

/// <summary>
/// Abstracción del runtime de contenedores del satélite. Aísla la lógica del worker
/// del runtime concreto (Docker via socket, Podman via CLI) y permite seleccionar
/// la implementación al arrancar según <c>Satellite:ContainerRuntime</c>.
/// <para>
/// Los contratos viven en <c>Aethra.Shared.Contracts.Containers</c> y son agnósticos
/// del backend: el central no necesita saber si la VM corre Docker o Podman.
/// </para>
/// </summary>
public interface IContainerRuntime
{
    /// <summary>Construye una imagen a partir de un tarball de contexto + Dockerfile.</summary>
    Task<BuildResult> BuildImageAsync(BuildSpec spec, CancellationToken ct);

    /// <summary>Sube una imagen ya construida al registry indicado.</summary>
    Task<PushResult> PushImageAsync(string imageRef, RegistryAuth auth, CancellationToken ct);

    /// <summary>Descarga una imagen del registry, autenticándose si <paramref name="auth"/> no es null.</summary>
    Task<PullResult> PullImageAsync(string imageRef, RegistryAuth? auth, CancellationToken ct);

    /// <summary>Crea y arranca un contenedor a partir del <see cref="RunSpec"/>.</summary>
    Task<RunResult> RunContainerAsync(RunSpec spec, CancellationToken ct);

    /// <summary>Detiene gracefully un contenedor (SIGTERM → timeout → SIGKILL).</summary>
    Task StopContainerAsync(string nameOrId, CancellationToken ct);

    /// <summary>Elimina un contenedor. Si <paramref name="force"/> es true, también lo mata si está corriendo.</summary>
    Task RemoveContainerAsync(string nameOrId, bool force, CancellationToken ct);

    /// <summary>Streamea las líneas de log del contenedor (stdout/stderr ya intercaladas).</summary>
    IAsyncEnumerable<string> StreamLogsAsync(string nameOrId, int tailLines, CancellationToken ct);

    /// <summary>Lista los contenedores conocidos por el runtime (incluidos los detenidos).</summary>
    Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(CancellationToken ct);

    /// <summary>F12.1A — ejecuta un comando shell (<c>sh -c "command"</c>) dentro de un
    /// contenedor corriendo. Captura stdout/stderr y exit code. Si el comando excede
    /// <paramref name="timeoutSeconds"/>, mata el proceso y marca <c>TimedOut=true</c>.</summary>
    Task<ExecResult> ExecInContainerAsync(
        string containerNameOrId, string command, int timeoutSeconds, CancellationToken ct);
}
