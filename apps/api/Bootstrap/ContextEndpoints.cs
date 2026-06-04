using Aethra.Modules.Cloudflare.UseCases.Zones.Queries;
using Aethra.Modules.Projects.UseCases.Projects.Queries;
using Aethra.Modules.Services.UseCases.Queries;
using Aethra.Modules.Vms.UseCases.Vms.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;

namespace Aethra.Api.Bootstrap;

/// <summary>
/// Endpoint <c>GET /context</c> — pensado para agentes IA (MCP) y para la UI.
/// Devuelve un snapshot agregado: proyectos, VMs, dominios Cloudflare, servicios gestionados.
///
/// Despacha las queries de lista reales de cada módulo. Cloudflare se envuelve en try/catch
/// porque puede no estar configurado (sin token) y no debe tumbar el snapshot completo.
/// </summary>
public static class ContextEndpoints
{
    public static IEndpointRouteBuilder MapContextEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/context", [Authorize] async (IMediator mediator, CancellationToken ct) =>
        {
            var projects = await mediator.Send(new ListProjectsQuery(), ct);
            var vms = await mediator.Send(new ListVmsQuery(), ct);
            var services = await mediator.Send(new ListServicesQuery(), ct);

            IEnumerable<object> zones = Array.Empty<object>();
            try
            {
                var z = await mediator.Send(new ListZonesQuery(), ct);
                if (z.IsSuccess)
                {
                    zones = (IEnumerable<object>)z.Value;
                }
            }
            catch
            {
                // Cloudflare puede no estar configurado; no rompe el snapshot.
            }

            return Results.Ok(new
            {
                projects = projects.IsSuccess ? (IEnumerable<object>)projects.Value : Array.Empty<object>(),
                vms = vms.IsSuccess ? (IEnumerable<object>)vms.Value : Array.Empty<object>(),
                services = services.IsSuccess ? (IEnumerable<object>)services.Value : Array.Empty<object>(),
                cloudflare_zones = zones,
                generated_at = DateTimeOffset.UtcNow,
            });
        })
        .WithName("Context")
        .WithTags("System");

        return app;
    }
}
