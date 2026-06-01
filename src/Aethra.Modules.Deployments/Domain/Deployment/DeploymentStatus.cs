namespace Aethra.Modules.Deployments.Domain.Deployment;

/// <summary>
/// State machine del pipeline de deployment (1 Build → N Deployments):
///
///   Pending
///     ↓
///   Pulling      → Failed (pull from registry error)
///     ↓
///   Starting     → Failed (run container error)
///     ↓
///   Healthcheck  → Failed (container no responde sano)
///     ↓
///   Swapping     → Failed (no se pudo actualizar Route YARP)
///                  + intento de Rollback → RolledBack
///     ↓
///   Completed
///
/// Cancelled puede llegar desde Pending o Pulling (no desde Starting ni después: el contenedor
/// nuevo ya puede estar arrancando y abortarlo dejaría estado parcial).
///
/// RolledBack es un estado terminal especial: se alcanza tras un Fail en Swapping (o post-Swap)
/// cuando el rollback al contenedor previo se ejecutó con éxito.
/// </summary>
public enum DeploymentStatus
{
    Pending = 0,
    Pulling = 1,
    Starting = 2,
    Healthcheck = 3,
    Swapping = 4,
    Completed = 5,
    Failed = 6,
    RolledBack = 7,
    Cancelled = 8,
}

public static class DeploymentStatusExtensions
{
    /// <summary>true mientras el deployment está activo (no en estado terminal).</summary>
    public static bool IsInProgress(this DeploymentStatus s) => s is
        DeploymentStatus.Pending or
        DeploymentStatus.Pulling or
        DeploymentStatus.Starting or
        DeploymentStatus.Healthcheck or
        DeploymentStatus.Swapping;

    /// <summary>true si el deployment ya terminó (Completed/Failed/RolledBack/Cancelled).</summary>
    public static bool IsTerminal(this DeploymentStatus s) => !s.IsInProgress();
}
