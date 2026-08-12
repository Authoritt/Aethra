namespace Aethra.Modules.Projects.Domain.Instances;

/// <summary>
/// Healthcheck Docker para una <see cref="Instance"/>.
///
/// <see cref="Test"/>: comando ejecutado dentro del contenedor (formato shell o exec).
///   Ejemplo exec: <c>["CMD", "curl", "-f", "http://localhost:8080/health"]</c>.
///   Ejemplo shell: <c>["CMD-SHELL", "curl -f http://localhost:8080/health || exit 1"]</c>.
/// <see cref="IntervalSeconds"/>: período entre ejecuciones.
/// <see cref="Retries"/>: fallos consecutivos antes de marcar unhealthy.
/// <see cref="TimeoutSeconds"/>: timeout por ejecución (<c>null</c> = default Docker).
/// <see cref="StartPeriodSeconds"/>: grace period inicial donde los fallos no cuentan
/// (útil para apps con cold start largo).
/// </summary>
/// <remarks>
/// Sealed record (no record struct): se persiste como JSON column en la <see cref="Instance"/>.
/// <see cref="Test"/> es <c>IReadOnlyList&lt;string&gt;</c> en lugar de <c>string[]</c> para
/// alinear con la convención del repo (aggregates siempre exponen <c>IReadOnlyList</c>).
/// </remarks>
public sealed record Healthcheck(
    IReadOnlyList<string> Test,
    int IntervalSeconds,
    int Retries,
    int? TimeoutSeconds = null,
    int? StartPeriodSeconds = null)
{
    /// <summary>
    /// ¿Es sensata esta configuración de healthcheck? Devuelve el motivo del rechazo, o <c>null</c>
    /// si es válida.
    ///
    /// <para>La comprobación NO va en el constructor, y es deliberado. EF materializa este record
    /// por su constructor posicional al leer la columna JSON: una guarda ahí haría que cualquier
    /// instancia ya guardada con valores que antes se aceptaban —intervalo cero, timeout no
    /// positivo, período de gracia negativo, comando vacío— reventara al LEERLA. El usuario
    /// recibiría un 500 al listar o reconfigurar, y no podría corregir la configuración porque para
    /// corregirla hay que poder leerla primero. Un invariante nuevo sobre datos viejos se aplica en
    /// el borde de entrada o se migra; nunca en la materialización.</para>
    /// </summary>
    public static string? Validate(
        IReadOnlyList<string>? test, int intervalSeconds, int retries, int? timeoutSeconds, int? startPeriodSeconds)
    {
        if (test is not { Count: > 0 })
        {
            // Sin comando el contenedor queda sin comprobación REAL mientras aparenta tener una
            // configurada, que es la peor de las dos formas de no tener healthcheck.
            return "El healthcheck necesita al menos un elemento en Test.";
        }
        if (intervalSeconds <= 0)
        {
            return "El intervalo del healthcheck debe ser mayor que cero.";
        }
        if (retries <= 0)
        {
            return "El número de reintentos del healthcheck debe ser mayor que cero.";
        }
        if (timeoutSeconds is { } t && t <= 0)
        {
            return "El timeout del healthcheck debe ser mayor que cero.";
        }
        // El período de gracia SÍ admite cero: "sin gracia" describe un comportamiento legítimo, a
        // diferencia de un intervalo cero, que no describe ninguna cadencia.
        if (startPeriodSeconds is { } sp && sp < 0)
        {
            return "El período de gracia del healthcheck no puede ser negativo.";
        }
        return null;
    }
}
