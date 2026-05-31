using System.ComponentModel;
using Aethra.Modules.Deployments.Domain;
using Aethra.Modules.Deployments.UseCases.Commands;
using Aethra.Modules.Deployments.UseCases.Queries;
using Aethra.Modules.Mcp.Security;
using Aethra.Shared.Contracts.Projects;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

[McpServerToolType]
public sealed class DeploymentsTools(IMediator mediator, IMcpCallerContext caller, IApplicationLookup appLookup)
{
    [McpServerTool(Name = "aethra_trigger_deploy", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Encola un deploy para una Application. Si git_sha es null, el worker resolverá HEAD del branch.")]
    public async Task<object> TriggerDeployAsync(
        [Description("ID de la Application (formato 'app_...').")] string applicationId,
        [Description("SHA del commit a deployar. Si null, se resuelve el HEAD del branch de la app.")] string? gitSha,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.DeploymentsTrigger))
        {
            return McpResponses.InsufficientScope(McpScopes.DeploymentsTrigger);
        }

        var app = await appLookup.GetByIdAsync(applicationId, ct).ConfigureAwait(false);
        if (app is null)
        {
            return McpResponses.Failure("application.not_found",
                $"Application '{applicationId}' no existe.", "not_found");
        }

        var cmd = new TriggerDeployCommand(
            ApplicationId: applicationId,
            GitSha: gitSha,
            Branch: app.Branch,
            Trigger: DeployTrigger.Manual,
            TriggeredBy: caller.AuditSource);
        var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_get_deploy_logs", ReadOnly = true, OpenWorld = false)]
    [Description("Recupera los chunks de log de un deploy, ordenados por sequence. Pasa since_sequence para tail incremental.")]
    public async Task<object> GetDeployLogsAsync(
        [Description("ID del deploy job (formato 'dpl_...').")] string jobId,
        [Description("Sequence number desde el cual leer (default 0).")] long sinceSequence,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.DeploymentsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.DeploymentsRead);
        }
        var result = await mediator.Send(new GetDeployLogsQuery(jobId, sinceSequence), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_list_deploys", ReadOnly = true, OpenWorld = false)]
    [Description("Lista los últimos N deploys de una Application, más recientes primero.")]
    public async Task<object> ListDeploysAsync(
        [Description("ID de la Application (formato 'app_...').")] string applicationId,
        [Description("Cantidad máxima a devolver (1-200, default 50).")] int limit,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.DeploymentsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.DeploymentsRead);
        }
        var effective = limit <= 0 ? 50 : limit;
        var result = await mediator.Send(new ListDeploysQuery(applicationId, effective), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }
}
