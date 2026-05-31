namespace Aethra.Modules.Monitoring.Domain;

/// <summary>
/// Método HTTP que el probe ejecutará contra <see cref="Monitor.Url"/>.
/// El alcance se mantiene reducido a los métodos comunes para uptime checks; agregar PUT/DELETE
/// no aporta valor en F6 y abre la puerta a usos accidentales como side-effect.
/// </summary>
public enum MonitorHttpMethod
{
    GET = 0,
    HEAD = 1,
    POST = 2,
}

/// <summary>
/// Resultado consolidado del estado del monitor en función de la última muestra.
/// <list type="bullet">
///   <item><c>Unknown</c>: aún no se ha ejecutado ningún check.</item>
///   <item><c>Up</c>: último check devolvió un código en <see cref="Monitor.ExpectedStatusCodes"/>.</item>
///   <item><c>Degraded</c>: último check devolvió 2xx pero no esperado (responde pero "mal").</item>
///   <item><c>Down</c>: timeout, error de red o código fuera del rango sano.</item>
/// </list>
/// </summary>
public enum MonitorStatus
{
    Unknown = 0,
    Up = 1,
    Degraded = 2,
    Down = 3,
}
