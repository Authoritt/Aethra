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
    /// Devuelve todas las Instances asociadas a un Client. Útil para vistas tenant-céntricas.
    /// </summary>
    Task<IReadOnlyList<InstanceForDeployView>> FindByClientAsync(
        string clientId, CancellationToken ct);
}

/// <summary>
/// Proyección read-only de una Instance con los campos necesarios para orquestar un deploy
/// (target VM, container name, routing).
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
    int? PrimaryContainerPort);
