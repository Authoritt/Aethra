using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Projects.UseCases.Projects.Commands;
using Aethra.Modules.Projects.UseCases.Projects.Queries;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

[McpServerToolType]
public sealed class ProjectsTools(IMediator mediator, IMcpCallerContext caller)
{
    [McpServerTool(Name = "aethra_list_projects", ReadOnly = true, OpenWorld = false)]
    [Description("Lista todos los proyectos con su jerarquía completa (environments + applications).")]
    public async Task<object> ListAsync(CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsRead);
        }
        var result = await mediator.Send(new ListProjectsQuery(), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_get_project", ReadOnly = true, OpenWorld = false)]
    [Description("Detalle completo de un proyecto: environments, applications con su source/build/runtime.")]
    public async Task<object> GetAsync(
        [Description("ID del proyecto (formato 'prj_...').")] string projectId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsRead);
        }
        var result = await mediator.Send(new GetProjectByIdQuery(projectId), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_discover_repo", ReadOnly = true, OpenWorld = true)]
    [Description("Analiza un repo Git y propone aplicaciones para crear. F1: heurística sin clonar; F4.5 hará clone shallow real.")]
    public async Task<object> DiscoverRepoAsync(
        [Description("URL del repositorio Git (HTTPS o SSH).")] string repoUrl,
        [Description("Branch a inspeccionar. Default: 'main'.")] string? branch,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsRead);
        }
        var cmd = new DiscoverRepoCommand(repoUrl, branch);
        var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_create_application_from_git", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Crea una Application a partir de un repo Git. F4.5 entregará el wiring completo de discover + create. Por ahora devuelve not_implemented con la fase prevista.")]
    public object CreateApplicationFromGit(
        [Description("URL del repositorio Git.")] string repoUrl,
        [Description("ID del proyecto destino (opcional — si null se sugerirá crear uno).")] string? projectId,
        [Description("ID de la VM target. Opcional — se inferirá del default.")] string? targetVmId,
        [Description("Hostname público sugerido (opcional, para el route + DNS).")] string? suggestedHostname,
        [Description("Vars iniciales como dict key→value (opcional).")] Dictionary<string, string>? envVars,
        [Description("Si true, no crea — sólo simula y devuelve un plan.")] bool dryRun = false)
    {
        // Tocar parámetros para evitar CA1801 sin gold-plating.
        _ = repoUrl;
        _ = projectId;
        _ = targetVmId;
        _ = suggestedHostname;
        _ = envVars;
        _ = dryRun;
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        // F1-F6 cerradas; F4 implementó deploys con TriggerDeployCommand pero no expuso
        // CreateApplicationCommand. La fase F4.5 cableará el discover→create→attach completo.
        return McpResponses.NotImplemented("aethra_create_application_from_git", "F4.5");
    }
}
