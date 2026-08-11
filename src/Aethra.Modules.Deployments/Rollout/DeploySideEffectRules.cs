namespace Aethra.Modules.Deployments.Rollout;

/// <summary>Cómo terminó un efecto colateral idempotente del deploy (ruta, monitor).</summary>
public enum SideEffectOutcome
{
    /// <summary>Se creó ahora.</summary>
    Created = 0,

    /// <summary>Ya existía: es el caso NORMAL de un redeploy, no un problema.</summary>
    AlreadyExists = 1,

    /// <summary>Falló de verdad. Nunca se puede confundir con <see cref="AlreadyExists"/>.</summary>
    Failed = 2,
}

/// <summary>
/// OT-006 <c>#52</c>/<c>#53</c> — separa "ya existía" de "falló" en los comandos idempotentes que el
/// deploy nativo dispara después de levantar los contenedores.
///
/// <para>
/// <c>#52</c>: <c>NativeDeployRunner.ReconcileRoutingAsync</c> etiquetaba CUALQUIER fallo de
/// <c>CreateRouteCommand</c> como <c>"(ya existía)"</c>. Pero ese comando solo devuelve
/// <see cref="RouteAlreadyExistsCode"/> cuando la ruta existe; sus otros fallos son
/// <c>route.invalid_backend</c>, el error de <c>Hostname.Create</c>, o el corto-circuito del
/// <c>ValidationBehavior</c>. Todos ellos significan que la URL NO va a servir, y se estaban
/// reportando como éxito. Si un fallo no se propaga, el rollout no puede saber que falló.
/// </para>
///
/// <para>
/// <c>#53</c>: el resultado de <c>CreateMonitorCommand</c> ni siquiera se asignaba. Un monitor que no
/// se crea deja a la app sin vigilancia y nadie se entera. Aquí sus dos conflictos benignos
/// (<see cref="MonitorSlugTakenCode"/>, <see cref="MonitorUrlTakenCode"/>) son el caso normal del
/// redeploy; cualquier otro error es un fallo que debe quedar visible.
/// </para>
///
/// <para>
/// Recibe primitivos (<c>bool</c> + código de error) y no <c>Result&lt;T&gt;</c> a propósito:
/// <c>Aethra.Modules.Deployments</c> no referencia otros <c>Modules.*</c> (regla de aislamiento
/// declarada en su <c>.csproj</c>), y así la regla se prueba sin arrastrar Proxy ni Monitoring.
/// </para>
/// </summary>
public static class DeploySideEffectRules
{
    /// <summary>
    /// Único código con el que <c>CreateRouteHandler</c> señala "ya existe una ruta para ese
    /// hostname+path". Debe coincidir EXACTO con el literal del handler.
    /// </summary>
    public const string RouteAlreadyExistsCode = "route.hostname_taken";

    /// <summary>Conflicto benigno de <c>CreateMonitorHandler</c>: ya hay un monitor con ese slug.</summary>
    public const string MonitorSlugTakenCode = "monitor.slug_taken";

    /// <summary>Conflicto benigno de <c>CreateMonitorHandler</c>: ya hay un monitor para esa URL.</summary>
    public const string MonitorUrlTakenCode = "monitor.url_taken";

    /// <summary>
    /// Clasifica el resultado de un <c>CreateRouteCommand</c>. Un fallo con código desconocido o
    /// vacío es <see cref="SideEffectOutcome.Failed"/>, nunca <see cref="SideEffectOutcome.AlreadyExists"/>:
    /// la lista de códigos benignos es CERRADA, de modo que un código nuevo se rompe hacia el lado
    /// ruidoso y no hacia el lado silencioso.
    /// </summary>
    public static SideEffectOutcome ClassifyRoute(bool isSuccess, string? errorCode)
    {
        if (isSuccess)
        {
            return SideEffectOutcome.Created;
        }
        return string.Equals(errorCode, RouteAlreadyExistsCode, StringComparison.Ordinal)
            ? SideEffectOutcome.AlreadyExists
            : SideEffectOutcome.Failed;
    }

    /// <summary>
    /// Clasifica el resultado de un <c>CreateMonitorCommand</c>. Mismo criterio de lista cerrada que
    /// <see cref="ClassifyRoute"/>.
    /// </summary>
    public static SideEffectOutcome ClassifyMonitor(bool isSuccess, string? errorCode)
    {
        if (isSuccess)
        {
            return SideEffectOutcome.Created;
        }
        return string.Equals(errorCode, MonitorSlugTakenCode, StringComparison.Ordinal)
            || string.Equals(errorCode, MonitorUrlTakenCode, StringComparison.Ordinal)
                ? SideEffectOutcome.AlreadyExists
                : SideEffectOutcome.Failed;
    }
}
