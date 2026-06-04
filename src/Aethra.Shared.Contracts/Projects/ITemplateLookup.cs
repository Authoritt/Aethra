namespace Aethra.Shared.Contracts.Projects;

/// <summary>
/// Read-model cross-module: permite que módulos externos (Deployments, Webhooks, etc.)
/// consulten Templates sin referenciar internals de <c>Modules.Projects</c>.
///
/// La implementación EF Core viene en la sub-fase F9.0 persistence; mientras tanto el
/// host registra <c>NoOpTemplateLookup</c> para que la solución compile sin datos reales.
/// </summary>
public interface ITemplateLookup
{
    /// <summary>
    /// Devuelve todos los Templates que apuntan al mismo (repo, branch). Vacío si no hay matches.
    /// Usado por el webhook handler para hacer fan-out hacia los Templates afectados por un push.
    /// </summary>
    Task<IReadOnlyList<TemplateForBuildView>> FindByRepoAsync(
        string repoUrl, string branch, CancellationToken ct);

    /// <summary>
    /// F12.3 — devuelve todos los Templates configurados al mismo repo Git, sin filtrar por branch.
    /// El handler de <c>pull_request</c> lo usa porque el PR puede tener <c>base.ref</c> diferente
    /// al <c>DefaultBranch</c> del Template.
    /// </summary>
    Task<IReadOnlyList<TemplateForBuildView>> FindAllByRepoAsync(string repoUrl, CancellationToken ct);

    /// <summary>
    /// Devuelve un Template por su ID o null si no existe.
    /// </summary>
    Task<TemplateForBuildView?> GetByIdAsync(string templateId, CancellationToken ct);
}

/// <summary>
/// Proyección read-only de un Template con los campos necesarios para orquestar un build
/// (clone Git + decisión Dockerfile/Compose/Nixpacks).
/// </summary>
/// <param name="ComposeFilePath">Ruta al <c>docker-compose.yml</c> si <c>BuildType=DockerCompose</c>; null en otros modos.</param>
/// <param name="AutoPreviewPullRequests">F12.3 — flag para que el webhook handler decida si auto-genera previews al recibir un PR.</param>
public sealed record TemplateForBuildView(
    string TemplateId,
    string ProjectId,
    string Slug,
    string Name,
    string GitRepoUrl,
    string Branch,
    string WebhookSecret,
    string BaseDirectory,
    IReadOnlyList<string> WatchPaths,
    string BuildType,
    string DockerfilePath,
    string? ComposeFilePath = null,
    bool AutoPreviewPullRequests = false,
    IReadOnlyList<TemplateServiceView>? Services = null);

/// <summary>
/// F13 — proyección de un servicio multi-contenedor del Template (deploy nativo).
/// </summary>
public sealed record TemplateServiceView(
    string Name,
    string Image,
    int Port,
    IReadOnlyList<string> PathPrefixes,
    IReadOnlyList<KeyValuePair<string, string>> Env);
