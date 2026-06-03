namespace Aethra.Shared.Contracts.Projects;

/// <summary>
/// Read-model cross-module: permite que Deployments, Monitoring, Cloudflare, etc. consulten
/// Instances sin referenciar internals de <c>Modules.Projects</c>.
///
/// La implementación EF Core viene en la sub-fase F9.0 persistence; mientras tanto el host
/// registra <c>NoOpInstanceLookup</c>.
/// </summary>
public interface IInstanceLookup
{
    /// <summary>
    /// Devuelve una Instance por su ID o null si no existe.
    /// </summary>
    Task<InstanceForDeployView?> GetByIdAsync(string instanceId, CancellationToken ct);

    /// <summary>
    /// Devuelve todas las Instances asociadas a un Template. Si <paramref name="autoDeployOnly"/>
    /// es <c>true</c>, filtra solo las que tienen <c>AutoDeployOnNewBuild = true</c> — útil para
    /// el fan-out automático tras un build exitoso.
    /// </summary>
    Task<IReadOnlyList<InstanceForDeployView>> FindByTemplateAsync(
        string templateId, bool autoDeployOnly, CancellationToken ct);

    /// <summary>
    /// F12.3 — Devuelve las Instances de un Template cuyo <c>TrackedRef</c> efectivo (resuelto vía
    /// cascada Template → EnvironmentMapping → DefaultBranch) coincide con
    /// <paramref name="gitRef"/>. Filtra opcionalmente por <c>AutoDeployOnNewBuild</c>. Las
    /// Instances ephemerals (<c>IsEphemeral=true</c>) se incluyen porque tras un push a
    /// <c>refs/pull/N/head</c> SI deben redeployar.
    /// </summary>
    Task<IReadOnlyList<InstanceForDeployView>> FindByTrackedRefAsync(
        string templateId, string gitRef, bool autoDeployOnly, CancellationToken ct);

    /// <summary>
    /// Devuelve todas las Instances asociadas a un Client. Útil para vistas tenant-céntricas.
    /// </summary>
    Task<IReadOnlyList<InstanceForDeployView>> FindByClientAsync(
        string clientId, CancellationToken ct);
}

/// <summary>
/// Proyección read-only de una Instance con los campos necesarios para orquestar un deploy
/// (target VM, container name, routing). F12.3 agrega <c>TrackedRef</c> y <c>IsEphemeral</c>
/// para que el fan-out de Deployments filtre por ref y los handlers conozcan el lifecycle.
/// </summary>
public sealed record InstanceForDeployView(
    string InstanceId,
    string TemplateId,
    string ClientId,
    string ProjectId,
    string Slug,
    string Environment,
    string TargetVmId,
    string ContainerName,
    bool AutoDeployOnNewBuild,
    string? CustomDomain,
    string? AutoHostname,
    int? PrimaryContainerPort,
    string? TrackedRef = null,
    bool IsEphemeral = false,
    string? CreatedByUserId = null);
