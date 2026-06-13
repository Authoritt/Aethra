using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Projects.UseCases.Projects.Queries;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

/// <summary>
/// Herramientas para gestionar Projects/Templates/Clients/Instances. En F9.0 el módulo Projects
/// está sin UseCases (en proceso de A1); estas tools quedan stubeadas devolviendo
/// <c>not_implemented_post_refactor</c> hasta que F9.5 las reescriba.
/// </summary>
[McpServerToolType]
public sealed class ProjectsTools(IMediator mediator, IMcpCallerContext caller)
{
    [McpServerTool(Name = "aethra_list_projects", ReadOnly = true, OpenWorld = false)]
    [Description("Lista todos los proyectos con sus contadores de templates y clients. Read-only.")]
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
    [Description("Detalle de un proyecto: slug, nombre, descripción, color, icon, contadores de templates/clients "
        + "y timestamps. Read-only.")]
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
    [Description("(F9 stub) Analiza un repo Git para proponer Templates en F9.5.")]
    public object DiscoverRepo(
        [Description("URL del repositorio Git.")] string repoUrl,
        [Description("Branch a inspeccionar. Default: 'main'.")] string? branch)
    {
        _ = repoUrl;
        _ = branch;
        if (!caller.HasScope(McpScopes.ProjectsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsRead);
        }
        return McpResponses.NotImplemented("aethra_discover_repo", "F9.5");
    }

    [McpServerTool(Name = "aethra_create_application_from_git", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("(F9 stub) Se reescribirá en F9.5 como create_template_from_git + create_instance.")]
    public object CreateApplicationFromGit(
        [Description("URL del repositorio Git.")] string repoUrl,
        [Description("ID del proyecto destino.")] string? projectId,
        [Description("ID de la VM target.")] string? targetVmId,
        [Description("Hostname público sugerido.")] string? suggestedHostname,
        [Description("Vars iniciales como dict key→value.")] Dictionary<string, string>? envVars,
        [Description("Si true, no crea — sólo simula y devuelve un plan.")] bool dryRun = false)
    {
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
        return McpResponses.NotImplemented("aethra_create_application_from_git", "F9.5");
    }
}
