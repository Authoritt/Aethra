namespace Aethra.Modules.Deployments.Domain;

/// <summary>
/// State machine del deploy:
///
///   Queued
///     ↓
///   Cloning  → Failed (clone error, git auth, etc.)
///     ↓
///   Building → Failed (Dockerfile inválido, build error)
///     ↓
///   Healthcheck → Failed (contenedor nuevo no responde sano)
///     ↓
///   Swapping → Failed (no se pudo actualizar ruta YARP o stop viejo)
///     ↓
///   Completed
///
/// Cancelled puede llegar desde Queued, Cloning, Building (no desde Swapping ni después).
/// </summary>
public enum DeployStatus
{
    Queued = 0,
    Cloning = 1,
    Building = 2,
    Healthcheck = 3,
    Swapping = 4,
    Completed = 5,
    Failed = 6,
    Cancelled = 7,
}

public static class DeployStatusExtensions
{
    /// <summary>true mientras el deploy está activo (no en estado terminal).</summary>
    public static bool IsInProgress(this DeployStatus s) => s is
        DeployStatus.Queued or
        DeployStatus.Cloning or
        DeployStatus.Building or
        DeployStatus.Healthcheck or
        DeployStatus.Swapping;

    public static bool IsTerminal(this DeployStatus s) => !s.IsInProgress();
}
