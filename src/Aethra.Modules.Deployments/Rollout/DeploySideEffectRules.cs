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
    /// Clasifica el resultado de un <c>CreateRouteCommand</c>.
    ///
    /// <para><b>El código de error NO basta</b> (G2 de OT-006, hallazgo B4). <c>CreateRouteHandler</c>
    /// devuelve <see cref="RouteAlreadyExistsCode"/> ante CUALQUIER ruta que ya ocupe ese
    /// <c>(Hostname, PathPrefix)</c> —su guarda es <c>db.Routes.AnyAsync(r =&gt; r.Hostname == h
    /// &amp;&amp; r.PathPrefix == p)</c>— <b>sin comparar el backend ni el origin</b>, y ante el
    /// conflicto NO actualiza la fila. Tratarlo como benigno por el código significa reportar un
    /// deploy exitoso mientras el host sirve el backend de OTRA instancia: el trafico del cliente va
    /// a una aplicacion ajena y nadie se entera. Por eso "ya existía" solo es benigno si la ruta que
    /// ya está apunta a MI backend.</para>
    ///
    /// <para><b>Falla cerrado</b>: si no se pudo averiguar el backend existente
    /// (<paramref name="existingBackend"/> nulo o vacío, p. ej. porque el listado de rutas falló o
    /// porque la ruta apareció después de la foto) el resultado es
    /// <see cref="SideEffectOutcome.Failed"/>. No poder demostrar que la ruta es mía no es lo mismo
    /// que demostrar que lo es.</para>
    /// </summary>
    /// <param name="desiredBackend">Backend que este deploy quiere servir en ese host+path.</param>
    /// <param name="existingBackend">Backend de la ruta que YA ocupa ese host+path, si se conoce.</param>
    public static SideEffectOutcome ClassifyRoute(
        bool isSuccess, string? errorCode, string desiredBackend, string? existingBackend)
    {
        if (isSuccess)
        {
            return SideEffectOutcome.Created;
        }
        if (!string.Equals(errorCode, RouteAlreadyExistsCode, StringComparison.Ordinal))
        {
            return SideEffectOutcome.Failed;
        }
        return SameBackend(desiredBackend, existingBackend)
            ? SideEffectOutcome.AlreadyExists
            : SideEffectOutcome.Failed;
    }

    /// <summary>
    /// Clasifica el resultado de un <c>CreateMonitorCommand</c> (G2 de OT-006, hallazgo B5).
    ///
    /// <para><see cref="MonitorUrlTakenCode"/> es benigno sin más: su guarda compara la URL
    /// normalizada, así que prueba que ESA url ya está vigilada. <see cref="MonitorSlugTakenCode"/>
    /// no prueba nada equivalente —solo que el nombre está ocupado—, y si lo ocupa el monitor de
    /// otra aplicación, la mía se queda sin vigilancia en silencio.</para>
    ///
    /// <para>El grano de la comprobación es el <b>host</b>, no la URL exacta, y es deliberado:
    /// un operador puede haber apuntado el monitor a un endpoint mejor que la raíz (caso real en
    /// producción: el monitor <c>ekippo</c> vigila <c>/login</c> en vez de <c>/</c>). Esa app SÍ está
    /// vigilada; exigir igualdad exacta convertiría cada redeploy en una falsa alarma. Lo que hay que
    /// detectar es que el slug lo tenga un monitor de OTRO host.</para>
    ///
    /// <para><b>Falla cerrado</b>: URL desconocida o no parseable ⇒ <see cref="SideEffectOutcome.Failed"/>.</para>
    /// </summary>
    /// <param name="desiredUrl">URL que este deploy quiere vigilar.</param>
    /// <param name="existingUrlForSlug">URL del monitor que ya usa ese slug, si se conoce.</param>
    public static SideEffectOutcome ClassifyMonitor(
        bool isSuccess, string? errorCode, string desiredUrl, string? existingUrlForSlug)
    {
        if (isSuccess)
        {
            return SideEffectOutcome.Created;
        }
        if (string.Equals(errorCode, MonitorUrlTakenCode, StringComparison.Ordinal))
        {
            return SideEffectOutcome.AlreadyExists;
        }
        if (!string.Equals(errorCode, MonitorSlugTakenCode, StringComparison.Ordinal))
        {
            return SideEffectOutcome.Failed;
        }
        return SameHost(desiredUrl, existingUrlForSlug)
            ? SideEffectOutcome.AlreadyExists
            : SideEffectOutcome.Failed;
    }

    /// <summary>
    /// Igualdad de backend tolerante a lo que no cambia el destino: espacios, barra final y
    /// diferencias de caja (esquema y host son case-insensitive por RFC 3986, y los backends que
    /// genera el deploy son <c>http://{slug}-{svc}:{puerto}</c>, siempre en minúsculas). El puerto
    /// SÍ cuenta: <c>http://app:8080</c> y <c>http://app:9090</c> son destinos distintos.
    /// </summary>
    private static bool SameBackend(string desired, string? existing)
    {
        if (string.IsNullOrWhiteSpace(existing) || string.IsNullOrWhiteSpace(desired))
        {
            return false;
        }
        return string.Equals(
            desired.Trim().TrimEnd('/'), existing.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compara el host de dos URLs absolutas. Cualquier cosa que no sea una URL absoluta parseable
    /// (nula, vacía, relativa, basura) devuelve <c>false</c>: no se puede afirmar que sea el mismo host.
    /// </summary>
    private static bool SameHost(string desiredUrl, string? existingUrl)
    {
        if (string.IsNullOrWhiteSpace(existingUrl) || string.IsNullOrWhiteSpace(desiredUrl))
        {
            return false;
        }
        if (!Uri.TryCreate(desiredUrl.Trim(), UriKind.Absolute, out var a)
            || !Uri.TryCreate(existingUrl.Trim(), UriKind.Absolute, out var b))
        {
            return false;
        }
        return string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase);
    }
}
