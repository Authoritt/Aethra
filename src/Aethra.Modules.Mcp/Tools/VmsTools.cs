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

    [McpServerTool(Name = "aethra_cluster_disk_overview", ReadOnly = true, OpenWorld = false)]
    [Description("Resumen de disco RAÍZ por VM del clúster: total/libre y % libre de cada nodo, total "
        + "distribuible (suma del libre de los Connected) y qué nodos tienen capacidad de sobra. Sirve para "
        + "decidir dónde distribuir backups/artefactos (satellite://auto va al nodo Connected con más disco "
        + "libre). Read-only; sólo números de disco + slug/estado (sin datos sensibles).")]
    public async Task<object> ClusterDiskOverviewAsync(CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.VmsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.VmsRead);
        }
        var result = await mediator.Send(new ListVmsQuery(), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return McpResponses.FromError(result.Error);
        }

        var nodes = result.Value
            .Where(v => v.RootDiskTotalBytes is > 0)
            .Select(v =>
            {
                var total = v.RootDiskTotalBytes ?? 0L;
                var free = v.RootDiskAvailableBytes ?? 0L;
                var connected = string.Equals(v.Status, "Connected", StringComparison.OrdinalIgnoreCase);
                return new
                {
                    vm_id = v.Id,
                    slug = v.Slug,
                    status = v.Status,
                    root_total_bytes = total,
                    root_free_bytes = free,
                    free_percent = total > 0 ? Math.Round(100.0 * free / total, 1) : 0,
                    connected,
                    has_spare = connected && total > 0 && (double)free / total >= 0.5,
                };
            })
            .OrderByDescending(n => n.root_free_bytes)
            .ToList();

        return McpResponses.Ok(new
        {
            nodes,
            node_count = nodes.Count,
            distributable_free_bytes = nodes.Where(n => n.connected).Sum(n => n.root_free_bytes),
            note = "satellite://auto coloca el backup en el nodo Connected con más disco libre.",
        });
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
        + "true = la VM es candidata para correr previews; false = sólo workloads permanentes."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
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
        if (!result.IsSuccess)
        {
            return McpResponses.FromError(result.Error);
        }
        // Lo guardado, no lo pedido: ver McpWriteBack e issue #27.
        return await McpWriteBack.ConfirmarAsync(
            c => mediator.Send(new Aethra.Modules.Vms.UseCases.Vms.Queries.GetVmByIdQuery(vmId), c),
            v => new { vm_id = v.Id, accepts_previews = v.AcceptsPreviews, state_confirmed = true },
            motivo => new { vm_id = vmId, written = true, state_confirmed = false,
                note = McpWriteBack.NotaSinConfirmar, readback_error = motivo },
            ct).ConfigureAwait(false);
    }
}
