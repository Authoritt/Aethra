using Aethra.Modules.Proxy.UseCases.Dtos;

namespace Aethra.Modules.Proxy.UseCases.Routes;

/// <summary>
/// OT-001 — regla de propiedad que un reconciliador de deploy (ej. <c>NativeDeployRunner</c> en
/// <c>apps/api</c>) usa para decidir qué rutas puede borrar como "obsoletas mías" sin arriesgar rutas
/// creadas por fuera. Vive en el módulo Proxy (junto al DTO y el campo <see cref="RouteDto.Origin"/>
/// que consume) para poder testearla sin arrastrar el host completo.
/// </summary>
public static class RouteOwnershipRules
{
    /// <summary>
    /// Origin que escribe el deploy nativo en toda ruta que crea (ver
    /// <c>NativeDeployRunner.ReconcileRoutingAsync</c>, paso 1). Debe coincidir EXACTO con la cadena
    /// que ese código pasa a <c>CreateRouteCommand</c>.
    /// </summary>
    public const string NativeDeployOrigin = "native_deploy";

    /// <summary>
    /// Decide si <paramref name="route"/> es candidata a borrado por un reconciliador de deploy nativo.
    /// Exige DOS condiciones, no solo el backend:
    /// <list type="number">
    /// <item>Apunta a uno de los backends actuales del deploy (<paramref name="myBackends"/>, prefijos
    /// <c>http://{slug}-{svc}:</c>).</item>
    /// <item>Su <see cref="RouteDto.Origin"/> es exactamente <see cref="NativeDeployOrigin"/>.</item>
    /// </list>
    /// Antes solo exigía (1): una ruta creada a mano (o por otro proceso) hacia el MISMO contenedor
    /// —caso real: los alias <c>yunke-*</c> apuntando a <c>factusforge-api</c>— era indistinguible de
    /// una ruta obsoleta del reconciliador y se borraba. Reproducido en prod 2026-08-07: el push
    /// <c>main@2deddd4</c> (deploy nativo completo de 4 servicios, vía webhook) borró las 4 rutas
    /// <c>yunke-*</c> porque compartían backend con <c>factusforge-*</c> — log
    /// <c>reconcile-routing factusforge: ruta obsoleta borrada yunke-*.authoritforge.dev/</c> a las
    /// 20:15:44-46 UTC. <c>Origin</c> nulo o distinto de <see cref="NativeDeployOrigin"/> (rutas
    /// creadas antes de que el campo existiera —quedan con <c>Origin=null</c>—, o por otro flujo:
    /// <c>"manual"</c>, <c>"backfill_backend"</c>, <c>"backfill_hostname"</c>...) se trata como
    /// "no es mía": la opción conservadora — preferimos dejar vivo un contenedor-huérfano ocasional a
    /// borrar una ruta que no creamos. Efecto secundario conocido: una ruta legítima del reconciliador
    /// pero anterior al campo <c>Origin</c> (ej. las <c>factusforge-*</c> originales, con
    /// <c>Origin="backfill_backend"</c>) tampoco se limpia sola si cambia de host — el comando de
    /// creación no actualiza el <c>Origin</c> de una ruta que ya existía (devuelve conflicto y no toca
    /// la fila), así que nunca migra a <see cref="NativeDeployOrigin"/> por sí sola. Deuda conocida,
    /// fuera del alcance de OT-001.
    /// </summary>
    public static bool IsObsoleteOwnRoute(
        RouteDto route, IReadOnlyList<string> myBackends, IReadOnlySet<(string Host, string Prefix)> desired)
    {
        var isMine = myBackends.Any(b => route.BackendUrl.StartsWith(b, StringComparison.Ordinal));
        var isOurs = string.Equals(route.Origin, NativeDeployOrigin, StringComparison.Ordinal);
        return isMine && isOurs && !desired.Contains((route.Hostname.ToLowerInvariant(), route.PathPrefix));
    }
}
