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

    /// <summary>Reinicia un contenedor manteniendo imagen, env y volumenes actuales.</summary>
    Task RestartContainerAsync(string nameOrId, CancellationToken ct);

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

    /// <summary>
    /// Retención de imágenes: borra los tags más antiguos de <paramref name="repository"/>
    /// (ej. <c>aethra/myapp-api</c>) dejando los <paramref name="keepLast"/> más recientes
    /// por fecha de creación. NO fuerza el borrado, por lo que nunca elimina una imagen en uso por
    /// un contenedor. Best-effort e idempotente: con <paramref name="keepLast"/> &lt;= 0 no hace
    /// nada. Devuelve los refs efectivamente borrados.
    /// </summary>
    Task<IReadOnlyList<string>> PruneImageRepoAsync(string repository, int keepLast, CancellationToken ct);

    /// <summary>
    /// Poda el build cache del runtime (capas intermedias de BuildKit/buildah que el flujo git-mode
    /// acumula sin límite — ~15 GB por ciclo de builds → fuga de disco). Acota el cache por DOS vías:
    /// <list type="bullet">
    /// <item><paramref name="keepStorageGb"/> &gt; 0 → tope DURO de tamaño (<c>--reserved-space</c>):
    /// deja a lo sumo esos GB del cache más reciente y borra el resto. Bound robusto frente a ráfagas
    /// de builds del mismo día (que un filtro por edad NO reclama).</item>
    /// <item><paramref name="maxAgeHours"/> &gt; 0 (usado sólo si no hay tope de tamaño) → borra cache
    /// no usado en las últimas N horas, conservando el reciente para rebuilds rápidos.</item>
    /// </list>
    /// No toca imágenes ni contenedores. Best-effort e idempotente: con ambos &lt;= 0 no hace nada.
    /// Devuelve un resumen legible (ej. <c>"Total reclaimed space: 12GB"</c>) o <c>null</c> si no
    /// aplicó o falló.
    /// </summary>
    Task<string?> PruneBuildCacheAsync(int maxAgeHours, int keepStorageGb, CancellationToken ct);

    /// <summary>
    /// Borra las imágenes colgantes (<c>&lt;none&gt;</c>: capas sin tag que quedan tras rebuildear un
    /// tag a una imagen nueva). Nunca toca imágenes con tag ni en uso. Best-effort e idempotente.
    /// Devuelve un resumen legible o <c>null</c> si no aplicó o falló.
    /// </summary>
    Task<string?> PruneDanglingImagesAsync(CancellationToken ct);

    /// <summary>
    /// Borra los volúmenes <b>anónimos</b> colgantes (dangling): los que el runtime creó con nombre
    /// hash de 64 hex (ej. <c>27b96b5d…7473df</c>) y ya no están montados por ningún contenedor —
    /// quedan tras eliminar contenedores con volúmenes anónimos y nadie los reclama → fuga de disco.
    /// <para>
    /// <b>Seguro por construcción:</b> SOLO toca nombres que matchean <c>^[0-9a-f]{64}$</c>, por lo
    /// que NUNCA borra named volumes de datos/DataProtection (<c>*-dpkeys</c>, <c>aethra-pgdata</c>,
    /// <c>*-almacen</c>, …) aunque su contenedor esté momentáneamente caído entre deploys (que el
    /// daemon también reporta como "dangling"). El borrado es sin --force: un volumen en uso lo
    /// rechaza el runtime y se omite. Best-effort e idempotente.
    /// </para>
    /// Devuelve un resumen legible (ej. <c>"volúmenes anónimos podados: 3"</c>) o <c>null</c> si no
    /// aplicó o falló.
    /// </summary>
    Task<string?> PruneAnonymousVolumesAsync(CancellationToken ct);
}
