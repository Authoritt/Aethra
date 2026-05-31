namespace Aethra.Shared.Contracts.Projects;

/// <summary>
/// Read-model cross-module: permite que módulos externos (Deployments, Proxy, etc.)
/// consulten Applications sin referenciar internals de Modules.Projects.
///
/// La implementación vive en Modules.Projects.Infrastructure y se registra en su <c>AddProjectsModule</c>.
/// </summary>
public interface IApplicationLookup
{
    /// <summary>
    /// Devuelve todas las Applications que apuntan al mismo (repo, branch). Vacío si no hay matches.
    /// </summary>
    Task<IReadOnlyList<ApplicationForDeployView>> FindByRepoAsync(
        string repoUrl, string branch, CancellationToken ct);

    /// <summary>
    /// Devuelve una Application por su ID o null si no existe.
    /// </summary>
    Task<ApplicationForDeployView?> GetByIdAsync(string applicationId, CancellationToken ct);
}

/// <summary>
/// Proyección read-only de una Application con los campos necesarios para orquestar un deploy.
/// </summary>
public sealed record ApplicationForDeployView(
    string ApplicationId,
    string EnvironmentId,
    string ProjectId,
    string Slug,
    string Name,
    string GitRepoUrl,
    string Branch,
    string WebhookSecret,
    string BaseDirectory,
    IReadOnlyList<string> WatchPaths,
    string TargetVmId,
    string ContainerName,
    int? PrimaryContainerPort,
    string BuildType,
    string BuildPath);
