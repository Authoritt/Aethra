using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Monitoring.UseCases.Queries;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

[McpServerToolType]
public sealed class MonitoringTools(IMediator mediator, IMcpCallerContext caller)
{
    [McpServerTool(Name = "aethra_list_monitors", ReadOnly = true, OpenWorld = false)]
    [Description("Lista monitores HTTP con filtros opcionales (instance_id, project_id, status, is_enabled).")]
    public async Task<object> ListAsync(
        [Description("Filtro opcional por instance_id.")] string? instanceId,
        [Description("Filtro opcional por project_id.")] string? projectId,
        [Description("Filtro opcional por status ('Up', 'Down', 'Degraded', 'Unknown').")] string? status,
        [Description("Filtro opcional por is_enabled.")] bool? isEnabled,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.MonitoringRead))
        {
            return McpResponses.InsufficientScope(McpScopes.MonitoringRead);
        }
        var q = new ListMonitorsQuery(instanceId, projectId, status, isEnabled);
        var result = await mediator.Send(q, ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_get_monitor_status", ReadOnly = true, OpenWorld = false)]
    [Description("Counts agregados de monitores por estado (up/down/degraded/unknown/disabled). Una sola query a BD.")]
    public async Task<object> SummaryAsync(CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.MonitoringRead))
        {
            return McpResponses.InsufficientScope(McpScopes.MonitoringRead);
        }
        var result = await mediator.Send(new GetMonitorSummaryQuery(), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }
}
