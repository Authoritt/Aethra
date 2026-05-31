using System.ComponentModel;
using Aethra.Modules.Cloudflare.UseCases.Zones.Queries;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Monitoring.UseCases.Queries;
using Aethra.Modules.Services.UseCases.Queries;
using Aethra.Modules.Vms.UseCases.Vms.Queries;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

/// <summary>
/// Herramientas de "overview" — el primer call que un agente IA hace para entender qué hay
/// en la instancia de Aethra. Todas son read-only.
///
/// F9.0 cleanup: la sección "projects/applications" se ha removido temporalmente porque
/// <c>Modules.Projects.UseCases.Projects.Queries</c> dejó de existir. F9.5 reintroducirá un
/// <c>ListProjectsQuery</c> sobre el nuevo modelo Template/Client/Instance.
/// </summary>
[McpServerToolType]
public sealed class ContextTools(IMediator mediator, IMcpCallerContext caller)
{
    [McpServerTool(Name = "aethra_list_context", ReadOnly = true, OpenWorld = false)]
    [Description("Resumen agregado del estado de Aethra: counts de VMs, servicios, dominios, monitores. (Projects/Templates/Instances vendrán en F9.5.)")]
    public async Task<object> ListContextAsync(CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ContextRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ContextRead);
        }

        // Lanzamos todas las queries en paralelo — son AsNoTracking, sin side-effects.
        var vmsTask = mediator.Send(new ListVmsQuery(), ct);
        var servicesTask = mediator.Send(new ListServicesQuery(), ct);
        var zonesTask = mediator.Send(new ListZonesQuery(), ct);
        var monitorSummaryTask = mediator.Send(new GetMonitorSummaryQuery(), ct);

        await Task.WhenAll(vmsTask, servicesTask, zonesTask, monitorSummaryTask).ConfigureAwait(false);

        var vms = vmsTask.Result.IsSuccess ? vmsTask.Result.Value : [];
        var services = servicesTask.Result.IsSuccess ? servicesTask.Result.Value : [];
        var zones = zonesTask.Result.IsSuccess ? zonesTask.Result.Value : [];
        var monitors = monitorSummaryTask.Result.IsSuccess ? monitorSummaryTask.Result.Value : null;

        return McpResponses.Ok(new
        {
            counts = new
            {
                vms = vms.Count,
                services = services.Count,
                cloudflare_zones = zones.Count,
                monitors = monitors?.Total ?? 0,
                monitors_up = monitors?.Up ?? 0,
                monitors_down = monitors?.Down ?? 0,
                monitors_degraded = monitors?.Degraded ?? 0,
            },
            vms = vms.Select(v => new
            {
                id = v.Id,
                slug = v.Slug,
                name = v.Name,
                status = v.Status,
            }),
            services = services.Select(s => new
            {
                id = s.Id,
                slug = s.Slug,
                type = s.Type,
                status = s.Status,
                bindings = s.BindingsCount,
            }),
            cloudflare_zones = zones.Select(z => new
            {
                id = z.Id,
                name = z.Name,
                status = z.Status,
                records = z.RecordsCount,
            }),
            generated_at = DateTimeOffset.UtcNow,
            projects_pending = "F9.5 reintroducirá Projects/Templates/Clients/Instances aquí.",
        });
    }
}
