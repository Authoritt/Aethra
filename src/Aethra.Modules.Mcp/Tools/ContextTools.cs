using System.ComponentModel;
using Aethra.Modules.Cloudflare.UseCases.Zones.Queries;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Monitoring.UseCases.Queries;
using Aethra.Modules.Projects.UseCases.Projects.Queries;
using Aethra.Modules.Services.UseCases.Queries;
using Aethra.Modules.Vms.UseCases.Vms.Queries;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

/// <summary>
/// Herramientas de "overview" — el primer call que un agente IA hace para entender qué hay
/// en la instancia de Aethra. Todas son read-only.
/// </summary>
[McpServerToolType]
public sealed class ContextTools(IMediator mediator, IMcpCallerContext caller)
{
    [McpServerTool(Name = "aethra_list_context", ReadOnly = true, OpenWorld = false)]
    [Description("Resumen agregado del estado de Aethra: counts de proyectos, VMs, servicios, dominios, monitores. Es el primer call de cualquier agente que se conecta.")]
    public async Task<object> ListContextAsync(CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ContextRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ContextRead);
        }

        // Lanzamos todas las queries en paralelo — son AsNoTracking, sin side-effects.
        var projectsTask = mediator.Send(new ListProjectsQuery(), ct);
        var vmsTask = mediator.Send(new ListVmsQuery(), ct);
        var servicesTask = mediator.Send(new ListServicesQuery(), ct);
        var zonesTask = mediator.Send(new ListZonesQuery(), ct);
        var monitorSummaryTask = mediator.Send(new GetMonitorSummaryQuery(), ct);

        await Task.WhenAll(projectsTask, vmsTask, servicesTask, zonesTask, monitorSummaryTask).ConfigureAwait(false);

        var projects = projectsTask.Result.IsSuccess ? projectsTask.Result.Value : [];
        var vms = vmsTask.Result.IsSuccess ? vmsTask.Result.Value : [];
        var services = servicesTask.Result.IsSuccess ? servicesTask.Result.Value : [];
        var zones = zonesTask.Result.IsSuccess ? zonesTask.Result.Value : [];
        var monitors = monitorSummaryTask.Result.IsSuccess ? monitorSummaryTask.Result.Value : null;

        var appCount = projects.Sum(p => p.Environments.Sum(e => e.Applications.Count));

        return McpResponses.Ok(new
        {
            counts = new
            {
                projects = projects.Count,
                applications = appCount,
                vms = vms.Count,
                services = services.Count,
                cloudflare_zones = zones.Count,
                monitors = monitors?.Total ?? 0,
                monitors_up = monitors?.Up ?? 0,
                monitors_down = monitors?.Down ?? 0,
                monitors_degraded = monitors?.Degraded ?? 0,
            },
            projects = projects.Select(p => new
            {
                id = p.Id,
                slug = p.Slug,
                name = p.Name,
                environments = p.Environments.Count,
                applications = p.Environments.Sum(e => e.Applications.Count),
            }),
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
        });
    }
}
