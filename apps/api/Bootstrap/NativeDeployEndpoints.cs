using Aethra.Shared.Contracts.Projects;
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

        // F13.6 — upsert de env vars runtime a nivel Instance. Permite sacar los valores por-ambiente
        // (BD, host público, Cors) del svc.Env del template (compartido) y ponerlos por instancia, de
        // modo que cada Instance del mismo template apunta a su propia BD/host sin clonar el template.
        app.MapPut("/api/instances/{instanceId}/env-vars", async (
            string instanceId,
            [FromBody] SetInstanceEnvVarsRequest body,
            IEnvVarWriter writer,
            CancellationToken ct) =>
        {
            var items = body?.Vars ?? [];
            if (items.Count == 0)
            {
                return Results.BadRequest(new { message = "vars no puede estar vacío." });
            }
            var upserts = items
                .Where(v => !string.IsNullOrWhiteSpace(v.Key))
                .Select(v => new EnvVarUpsert(v.Key.Trim(), v.Value ?? string.Empty, v.IsBuildTime, v.IsRuntime ?? true))
                .ToList();
            await writer.UpsertManyAsync(EnvVarScope.Instance, instanceId, "manual:api", upserts, ct);
            return Results.Ok(new { instanceId, count = upserts.Count, source = "manual:api" });
        })
        .RequireAuthorization("scope:projects:write")
        .WithName("SetInstanceEnvVars")
        .WithTags("Projects");

        // F13.11 — despliega el connector cloudflared como contenedor gestionado (flip a túnel remoto
        // desde la UI, sin tocar el systemd del host). Múltiples connectors --token son réplicas HA.
        app.MapPost("/api/cloudflare/tunnel/connector/deploy", async (
            [FromBody] DeployConnectorRequest? body,
            CloudflareConnectorDeployer deployer,
            CancellationToken ct) =>
        {
            var r = await deployer.DeployAsync(body?.VmId, ct);
            return r.Success
                ? Results.Ok(new { vmId = r.VmId, container = r.ContainerName })
                : Results.Problem(r.Error);
        })
        .RequireAuthorization("scope:cloudflare:write")
        .WithName("DeployCloudflareConnector")
        .WithTags("Cloudflare");

        return app;
    }

    public sealed record DeployNativeRequest(string? Hostname);

    public sealed record DeployConnectorRequest(string? VmId);

    public sealed record SetInstanceEnvVarsRequest(IReadOnlyList<InstanceEnvVarItem>? Vars);

    public sealed record InstanceEnvVarItem(string Key, string? Value, bool IsBuildTime = false, bool? IsRuntime = true);
}
