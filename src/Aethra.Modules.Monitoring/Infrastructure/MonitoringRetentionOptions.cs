namespace Aethra.Modules.Monitoring.Infrastructure;

/// <summary>
/// Retención de <c>MonitorCheck</c>: el MonitorWorker escribe una fila por monitor por intervalo y nunca
/// se purgaban → crecimiento ilimitado del disco. El worker borra las filas más viejas que
/// <see cref="RetentionDays"/>. Configurable vía sección "Monitoring" (env: Monitoring__RetentionDays).
/// </summary>
public sealed class MonitoringRetentionOptions
{
    /// <summary>Días de checks crudos a conservar. 0 o negativo = desactiva la purga. Default 7.</summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>Cada cuántas horas barrer y purgar. Default 6.</summary>
    public double SweepIntervalHours { get; set; } = 6;
}
