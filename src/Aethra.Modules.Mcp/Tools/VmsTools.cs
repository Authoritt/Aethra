using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Vms.UseCases.Vms.Commands;
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

    [McpServerTool(Name = "aethra_set_vm_accepts_previews", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Marca si una VM acepta despliegues de PREVIEW (entornos efímeros por PR/branch). "
        + "true = la VM es candidata para correr previews; false = sólo workloads permanentes.")]
    public async Task<object> SetAcceptsPreviewsAsync(
        [Description("ID de la VM (formato 'vm_...').")] string vmId,
        [Description("true = acepta previews; false = no.")] bool acceptsPreviews,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.VmsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.VmsWrite);
        }
        var result = await mediator
            .Send(new SetAcceptsPreviewsCommand(vmId, acceptsPreviews), ct)
            .ConfigureAwait(false);
        return result.IsSuccess
            ? McpResponses.Ok(new { vm_id = vmId, accepts_previews = acceptsPreviews })
            : McpResponses.FromError(result.Error);
    }
}
