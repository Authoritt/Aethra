namespace Aethra.Modules.Metrics.Infrastructure;

/// <summary>
/// Retención de métricas crudas. El satélite reporta cada pocos segundos y las filas NUNCA se purgaban
/// → crecimiento ilimitado del disco. El worker borra las filas más viejas que <see cref="RetentionDays"/>.
/// Configurable vía sección "Metrics" (env: Metrics__RetentionDays, Metrics__SweepIntervalHours).
/// </summary>
public sealed class MetricsRetentionOptions
{
    /// <summary>Días de métricas crudas a conservar. 0 o negativo = desactiva la purga. Default 7.</summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>Cada cuántas horas barrer y purgar. Default 6.</summary>
    public double SweepIntervalHours { get; set; } = 6;
}
