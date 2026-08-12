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
    /// Período entre ejecuciones, en segundos. Tiene que ser positivo: un intervalo de cero o
    /// negativo no describe ninguna cadencia, y el runtime lo rechaza o se comporta de forma
    /// indefinida. Antes se persistía tal cual y el fallo aparecía al arrancar el contenedor,
    /// lejos de donde se configuró.
    /// </summary>
    public int IntervalSeconds { get; } = IntervalSeconds > 0
        ? IntervalSeconds
        : throw new ArgumentOutOfRangeException(
            nameof(IntervalSeconds), IntervalSeconds, "El intervalo del healthcheck debe ser mayor que cero.");

    /// <summary>
    /// Fallos consecutivos antes de marcar el contenedor como no sano. Cero sería "márcalo enfermo
    /// sin haber fallado nunca", que no es una configuración que alguien quiera de verdad.
    /// </summary>
    public int Retries { get; } = Retries > 0
        ? Retries
        : throw new ArgumentOutOfRangeException(
            nameof(Retries), Retries, "El número de reintentos del healthcheck debe ser mayor que cero.");

    /// <summary>Timeout por ejecución. <c>null</c> deja el valor por defecto del runtime; si se da, positivo.</summary>
    public int? TimeoutSeconds { get; } = TimeoutSeconds is { } t && t <= 0
        ? throw new ArgumentOutOfRangeException(
            nameof(TimeoutSeconds), t, "El timeout del healthcheck debe ser mayor que cero.")
        : TimeoutSeconds;

    /// <summary>
    /// Gracia inicial durante la cual los fallos no cuentan. Admite cero —"sin gracia" es una
    /// elección legítima, a diferencia de un intervalo cero— pero no valores negativos.
    /// </summary>
    public int? StartPeriodSeconds { get; } = StartPeriodSeconds is { } s && s < 0
        ? throw new ArgumentOutOfRangeException(
            nameof(StartPeriodSeconds), s, "El período de gracia del healthcheck no puede ser negativo.")
        : StartPeriodSeconds;

    /// <summary>
    /// Comando a ejecutar. Sin comando no hay healthcheck: un test vacío produce una configuración
    /// que el runtime no puede ejecutar y que, además, deja al contenedor sin comprobación real
    /// mientras aparenta tenerla.
    /// </summary>
    public IReadOnlyList<string> Test { get; } = Test is { Count: > 0 }
        ? Test
        : throw new ArgumentException("El healthcheck necesita al menos un elemento en Test.", nameof(Test));
}
