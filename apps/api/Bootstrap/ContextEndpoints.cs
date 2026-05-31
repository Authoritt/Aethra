using Microsoft.AspNetCore.Authorization;

namespace Aethra.Api.Bootstrap;

/// <summary>
/// Endpoint <c>GET /context</c> — pensado para agentes IA (MCP) y para la UI.
/// Devuelve un snapshot agregado: proyectos, VMs, dominios Cloudflare, servicios gestionados.
///
/// F0: stub que devuelve estructura vacia. Se va poblando con cada fase.
/// </summary>
public static class ContextEndpoints
{
    public static IEndpointRouteBuilder MapContextEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/context", [Authorize] () => Results.Ok(new
        {
            projects = Array.Empty<object>(),
            vms = Array.Empty<object>(),
            services = Array.Empty<object>(),
            cloudflare_zones = Array.Empty<object>(),
            generated_at = DateTimeOffset.UtcNow,
        }))
        .WithName("Context")
        .WithTags("System");

        return app;
    }
}
