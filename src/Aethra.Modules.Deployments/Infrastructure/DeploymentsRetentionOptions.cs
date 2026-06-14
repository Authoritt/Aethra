namespace Aethra.Modules.Deployments.Infrastructure;

/// <summary>
/// Retención de Builds/Deployments y sus logs (BuildLog/DeploymentLog crecen línea por línea por cada
/// build/deploy y nunca se purgaban → fuga de disco, agravada por build-on-VM). El worker borra builds
/// y deployments anteriores a <see cref="RetentionDays"/> (sus logs caen por cascade/orden). Default 30d.
/// Sección "Deployments" (env: Deployments__RetentionDays).
/// </summary>
public sealed class DeploymentsRetentionOptions
{
    /// <summary>Días de builds/deployments a conservar. 0 o negativo = desactiva la purga. Default 30.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>Cada cuántas horas barrer y purgar. Default 12.</summary>
    public double SweepIntervalHours { get; set; } = 12;
}
