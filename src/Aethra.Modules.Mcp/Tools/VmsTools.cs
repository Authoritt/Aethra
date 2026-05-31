using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Vms.UseCases.Vms.Queries;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

[McpServerToolType]
public sealed class VmsTools(IMediator mediator, IMcpCallerContext caller)
{
    [McpServerTool(Name = "aethra_list_vms", ReadOnly = true, OpenWorld = false)]
    [Description("Lista las VMs registradas con su estado (Connected/Disconnected) e info reportada (CPU, RAM, hostname).")]
    public async Task<object> ListAsync(CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.VmsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.VmsRead);
        }
        var result = await mediator.Send(new ListVmsQuery(), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_get_vm", ReadOnly = true, OpenWorld = false)]
    [Description("Detalle de una VM por ID.")]
    public async Task<object> GetAsync(
        [Description("ID de la VM (formato 'vm_...').")] string vmId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.VmsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.VmsRead);
        }
        var result = await mediator.Send(new GetVmByIdQuery(vmId), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }
}
