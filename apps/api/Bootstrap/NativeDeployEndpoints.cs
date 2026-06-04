using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Aethra.Api.Bootstrap;

/// <summary>
/// F13 — endpoint manual del deploy NATIVO multi-contenedor de una Instance. La orquestación vive
/// en <see cref="NativeDeployRunner"/> (reutilizada por el auto-trigger de webhook).
/// </summary>
public static class NativeDeployEndpoints
{
    public static IEndpointRouteBuilder MapNativeDeployEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/instances/{instanceId}/deploy-native", async (
            string instanceId,
            [FromBody] DeployNativeRequest? body,
            NativeDeployRunner runner,
            CancellationToken ct) =>
        {
            var r = await runner.DeployAsync(instanceId, body?.Hostname, ct);
            if (!r.Success)
            {
                return Results.Problem(r.Error);
            }
            return Results.Ok(new
            {
                instanceId,
                hostname = r.Hostname,
                healthy = r.Healthy,
                services = r.Services,
                routes = r.Routes,
            });
        })
        .RequireAuthorization("scope:projects:write")
        .WithName("DeployInstanceNative")
        .WithTags("Deployments");

        return app;
    }

    public sealed record DeployNativeRequest(string? Hostname);
}
