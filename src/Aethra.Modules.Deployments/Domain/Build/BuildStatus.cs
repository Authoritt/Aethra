namespace Aethra.Modules.Deployments.Domain.Build;

/// <summary>
/// State machine del pipeline de build (1 commit → 1 imagen OCI):
///
///   Queued
///     ↓
///   Cloning  → Failed (git clone error)
///     ↓
///   Building → Failed (Dockerfile inválido, build error)
///     ↓
///   Pushing  → Failed (registry inaccesible o credenciales)
///     ↓
///   Completed
///
/// Cancelled puede llegar desde Queued, Cloning o Building (no desde Pushing ni después).
/// </summary>
public enum BuildStatus
{
    Queued = 0,
    Cloning = 1,
    Building = 2,
    Pushing = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6,
}

public static class BuildStatusExtensions
{
    /// <summary>true mientras el build está activo (no en estado terminal).</summary>
    public static bool IsInProgress(this BuildStatus s) => s is
        BuildStatus.Queued or
        BuildStatus.Cloning or
        BuildStatus.Building or
        BuildStatus.Pushing;

    /// <summary>true si el build ya terminó (Completed/Failed/Cancelled).</summary>
    public static bool IsTerminal(this BuildStatus s) => !s.IsInProgress();
}
