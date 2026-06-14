namespace Aethra.Modules.Services.Infrastructure.Scheduling;

/// <summary>
/// Retención de <c>ScheduledJobRun</c>: cada corrida guarda stdout/stderr (hasta ~64KB c/u) y nunca se
/// purgaban → crecimiento ilimitado. El worker borra corridas anteriores a <see cref="RunRetentionDays"/>.
/// Default 30d (el historial de jobs es más valioso/menos frecuente que métricas). Sección "ScheduledJobs".
/// </summary>
public sealed class ScheduledJobRunRetentionOptions
{
    /// <summary>Días de corridas a conservar. 0 o negativo = desactiva la purga. Default 30.</summary>
    public int RunRetentionDays { get; set; } = 30;

    /// <summary>Cada cuántas horas barrer y purgar. Default 12.</summary>
    public double SweepIntervalHours { get; set; } = 12;
}
