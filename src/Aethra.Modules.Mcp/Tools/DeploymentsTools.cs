using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

/// <summary>
/// Herramientas relacionadas con deploys. En F9.0 el módulo Deployments se vació para refactor;
/// estas tools quedan stubeadas devolviendo <c>not_implemented_post_refactor</c> hasta que F9.5
/// las reescriba sobre el nuevo modelo Template/Instance + Build/DeployTask.
/// </summary>
[McpServerToolType]
public sealed class DeploymentsTools(IMcpCallerContext caller)
{
    [McpServerTool(Name = "aethra_trigger_deploy", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("(F9 stub) Encolará un deploy para una Instance una vez que F9.5 reescriba esta tool.")]
    public object TriggerDeploy(
        [Description("ID de la Instance (formato 'ins_...').")] string instanceId,
        [Description("SHA del commit a deployar. Si null, se resuelve el HEAD del branch del Template.")] string? gitSha)
    {
        _ = instanceId;
        _ = gitSha;
        if (!caller.HasScope(McpScopes.DeploymentsTrigger))
        {
            return McpResponses.InsufficientScope(McpScopes.DeploymentsTrigger);
        }
        return McpResponses.NotImplemented("aethra_trigger_deploy", "F9.5");
    }

    [McpServerTool(Name = "aethra_get_deploy_logs", ReadOnly = true, OpenWorld = false)]
    [Description("(F9 stub) Recuperará logs de un deploy una vez que F9.5 reescriba esta tool.")]
    public object GetDeployLogs(
        [Description("ID del deploy task.")] string jobId,
        [Description("Sequence number desde el cual leer.")] long sinceSequence)
    {
        _ = jobId;
        _ = sinceSequence;
        if (!caller.HasScope(McpScopes.DeploymentsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.DeploymentsRead);
        }
        return McpResponses.NotImplemented("aethra_get_deploy_logs", "F9.5");
    }

    [McpServerTool(Name = "aethra_list_deploys", ReadOnly = true, OpenWorld = false)]
    [Description("(F9 stub) Listará los últimos deploys de una Instance una vez que F9.5 reescriba esta tool.")]
    public object ListDeploys(
        [Description("ID de la Instance (formato 'ins_...').")] string instanceId,
        [Description("Cantidad máxima a devolver.")] int limit)
    {
        _ = instanceId;
        _ = limit;
        if (!caller.HasScope(McpScopes.DeploymentsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.DeploymentsRead);
        }
        return McpResponses.NotImplemented("aethra_list_deploys", "F9.5");
    }
}
