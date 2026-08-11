using Aethra.Shared.Contracts.Containers;

namespace Aethra.Modules.Deployments.Rollout;

/// <summary>
/// Veredicto de salud de un contenedor derivado del texto que el runtime reporta en
/// <see cref="ContainerInfo.Status"/>. Es la única señal de healthcheck que el central tiene
/// del contenedor: el satélite copia literalmente el campo <c>Status</c> de Docker
/// (<c>DockerContainerRuntime.ListContainersAsync</c>) o el <c>Status ?? State</c> de Podman.
/// </summary>
public enum ContainerHealthState
{
    /// <summary>El contenedor no aparece en el listado del runtime (nunca se creó, o desapareció).</summary>
    Absent = 0,

    /// <summary>Existe pero no está sirviendo: <c>Created</c>, <c>Exited</c>, <c>Dead</c>, <c>Restarting</c>, pausado.</summary>
    NotRunning = 1,

    /// <summary>Corriendo con healthcheck declarado que todavía está en su periodo de arranque (<c>health: starting</c>).</summary>
    Starting = 2,

    /// <summary>Corriendo, pero su healthcheck declarado FALLA (<c>unhealthy</c>).</summary>
    Unhealthy = 3,

    /// <summary>Corriendo y —si la imagen declara healthcheck— pasándolo.</summary>
    Healthy = 4,
}

/// <summary>
/// Resultado agregado del healthcheck de un rollout: o TODOS los contenedores objetivo están sanos,
/// o hay una lista no vacía de motivos concretos por los que no lo están.
/// </summary>
/// <param name="AllHealthy">
/// <c>true</c> solo si hay al menos un contenedor objetivo y todos están <see cref="ContainerHealthState.Healthy"/>.
/// </param>
/// <param name="Blockers">Motivo por contenedor no sano, listo para el log del deployment y el error del resultado.</param>
public sealed record RolloutHealthVerdict(bool AllHealthy, IReadOnlyList<string> Blockers);

/// <summary>
/// OT-006 <c>#51</c> — decide si el reemplazo de un rollout nativo está SANO.
///
/// <para>
/// Antes, <c>NativeDeployRunner</c> decidía con <c>Status.StartsWith("Up")</c>. Ese predicado da
/// verdadero para <c>"Up 2 minutes (unhealthy)"</c> y para <c>"Up 3 seconds (health: starting)"</c>:
/// un contenedor cuyo healthcheck FALLA se declaraba sano y sustituía a la revisión anterior. El
/// predicado leía el <c>Up</c> de Docker (el proceso arrancó), no el healthcheck.
/// </para>
///
/// <para>
/// Función pura y sin I/O para poder probarla sin levantar el host: <c>apps/api</c> no tiene proyecto
/// de tests (crear uno contra el Exe rompe el restore con <c>NU1109</c>), así que la DECISIÓN vive
/// aquí y el runner solo orquesta el I/O. Mismo patrón que
/// <c>Aethra.Modules.Proxy.UseCases.Routes.RouteOwnershipRules</c> (OT-001).
/// </para>
///
/// <para>
/// Calibrado contra producción (<c>docker ps</c> en la VM de Aethra, 2026-08-11): los contenedores
/// desplegados por el runner nativo —<c>factusforge-*</c> (Yunke), <c>ekippo-*</c>, <c>relaycore-*</c>,
/// <c>paradoxbox-*</c>— reportan <c>"Up N días"</c> SIN sufijo de salud porque sus imágenes no declaran
/// <c>HEALTHCHECK</c>; el único de la VM que lo declara reporta <c>"Up 3 days (healthy)"</c>. Es decir:
/// para las apps de hoy el veredicto es idéntico al anterior (sin regresión), y el endurecimiento
/// aplica en cuanto una imagen declare healthcheck. Que las imágenes no lo declaren es un gap de
/// configuración, no de esta regla: <c>TemplateServiceView</c> no tiene campo de healthcheck y
/// <c>RunSpec.Healthcheck</c> se envía en <c>null</c> (ver gap en la OT-006).
/// </para>
/// </summary>
public static class ContainerHealthRules
{
    // Fragmentos que Docker/Podman añaden al Status cuando la imagen declara HEALTHCHECK.
    private const string UnhealthyMarker = "unhealthy";
    private const string StartingMarker = "health: starting";

    /// <summary>
    /// Traduce el texto de estado del runtime a un <see cref="ContainerHealthState"/>.
    /// El orden de las comprobaciones importa: <c>"Up 2 minutes (unhealthy)"</c> EMPIEZA por
    /// <c>"Up"</c>, así que los marcadores de salud se evalúan ANTES que el prefijo de ejecución.
    /// </summary>
    public static ContainerHealthState Evaluate(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return ContainerHealthState.Absent;
        }

        var s = status.Trim();

        // 1) Healthcheck declarado que falla. Gana sobre cualquier otra señal.
        if (s.Contains(UnhealthyMarker, StringComparison.OrdinalIgnoreCase))
        {
            return ContainerHealthState.Unhealthy;
        }
        // 2) Healthcheck declarado todavía en warm-up: NO es sano aún, pero tampoco es un fallo.
        if (s.Contains(StartingMarker, StringComparison.OrdinalIgnoreCase))
        {
            return ContainerHealthState.Starting;
        }
        // 3) Crash-loop: Docker lo reporta como "Restarting (N) X ago". No es sano por más que
        //    en el instante del muestreo el proceso exista.
        if (s.Contains("Restarting", StringComparison.OrdinalIgnoreCase))
        {
            return ContainerHealthState.NotRunning;
        }
        // 4) "Up 3 days (Paused)": el proceso está congelado, no atiende tráfico.
        if (s.Contains("Paused", StringComparison.OrdinalIgnoreCase))
        {
            return ContainerHealthState.NotRunning;
        }
        // 5) Corriendo: "Up N ..." (Docker) o "running" (State de Podman como fallback).
        if (s.StartsWith("Up", StringComparison.OrdinalIgnoreCase)
            || s.Equals("running", StringComparison.OrdinalIgnoreCase))
        {
            return ContainerHealthState.Healthy;
        }

        // Created / Exited (0) / Exited (137) / Dead / cualquier cosa que no sepamos leer.
        return ContainerHealthState.NotRunning;
    }

    /// <summary>
    /// Estado del contenedor <paramref name="containerName"/> dentro del listado del runtime.
    /// El nombre se compara <see cref="StringComparison.Ordinal"/> porque los nombres de contenedor
    /// son <c>{slug}-{servicio}</c>, generados por el propio deploy y case-sensitive en Docker.
    /// </summary>
    public static ContainerHealthState EvaluateService(
        string containerName, IReadOnlyList<ContainerInfo> containers)
        => Evaluate(Find(containerName, containers)?.Status);

    /// <summary>
    /// Veredicto del rollout completo.
    ///
    /// <para>
    /// Una lista de objetivos VACÍA devuelve <c>AllHealthy=false</c>, no <c>true</c>. Un
    /// <c>All()</c> sobre una colección vacía es verdadero por vacuidad: sería un falso verde —
    /// exactamente el modo de fallo que esta OT persigue (declarar sano lo que nadie verificó).
    /// </para>
    /// </summary>
    public static RolloutHealthVerdict EvaluateAll(
        IReadOnlyCollection<string> containerNames, IReadOnlyList<ContainerInfo> containers)
    {
        if (containerNames.Count == 0)
        {
            return new RolloutHealthVerdict(false,
                ["no hay contenedores objetivo que verificar (un healthcheck sin objetivos no es un healthcheck sano)"]);
        }

        var blockers = new List<string>();
        foreach (var name in containerNames)
        {
            var container = Find(name, containers);
            var state = Evaluate(container?.Status);
            if (state == ContainerHealthState.Healthy)
            {
                continue;
            }
            blockers.Add(container is null
                ? $"{name}: no aparece en el runtime ({state})"
                : $"{name}: {state} (estado del runtime: '{container.Status}')");
        }

        return new RolloutHealthVerdict(blockers.Count == 0, blockers);
    }

    private static ContainerInfo? Find(string containerName, IReadOnlyList<ContainerInfo> containers)
    {
        for (var i = 0; i < containers.Count; i++)
        {
            if (string.Equals(containers[i].Name, containerName, StringComparison.Ordinal))
            {
                return containers[i];
            }
        }
        return null;
    }
}
