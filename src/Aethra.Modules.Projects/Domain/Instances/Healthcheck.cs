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
    int? StartPeriodSeconds = null);
