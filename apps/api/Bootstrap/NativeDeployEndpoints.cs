using Aethra.Modules.Monitoring.UseCases.Commands;
using Aethra.Modules.Proxy.UseCases.Routes.Commands;
using Aethra.Shared.Contracts.Containers;
using Aethra.Shared.Contracts.Projects;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aethra.Api.Bootstrap;

/// <summary>
/// F13 — deploy NATIVO multi-contenedor de una Instance cuyo Template tiene <c>Services</c>.
/// Orquesta en el host (composition root) sin tocar el DeploymentOrchestrator single-container:
/// por cada servicio del template arranca un contenedor <c>{instanceSlug}-{service}</c> en
/// <c>aethra-net</c> con la imagen prebuilt y env resuelto (cascade + interpolación
/// <c>{instance}</c>), hace healthcheck, y crea las rutas YARP (host + pathPrefix) + un monitor.
/// </summary>
public static class NativeDeployEndpoints
{
    public static IEndpointRouteBuilder MapNativeDeployEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/instances/{instanceId}/deploy-native", async (
            string instanceId,
            [FromBody] DeployNativeRequest? body,
            IInstanceLookup instanceLookup,
            ITemplateLookup templateLookup,
            IEnvironmentResolver envResolver,
            ISatelliteRpcClient satellite,
            IMediator mediator,
            IConfiguration config,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("NativeDeploy");
            var appNetwork = config["Deployments:AppNetwork"] is { Length: > 0 } n ? n : "aethra-net";

            var instance = await instanceLookup.GetByIdAsync(instanceId, ct);
            if (instance is null)
            {
                return Results.NotFound(new { code = "instance.not_found", message = $"Instance '{instanceId}' no existe." });
            }
            var template = await templateLookup.GetByIdAsync(instance.TemplateId, ct);
            if (template is null)
            {
                return Results.NotFound(new { code = "template.not_found", message = "Template de la instance no existe." });
            }
            var services = template.Services ?? [];
            if (services.Count == 0)
            {
                return Results.UnprocessableEntity(new { code = "template.no_services", message = "El template no define servicios (Services). Configúralos con PUT /api/templates/{id}/services." });
            }

            var hostname = body?.Hostname ?? instance.CustomDomain ?? instance.AutoHostname;
            if (string.IsNullOrWhiteSpace(hostname))
            {
                return Results.UnprocessableEntity(new { code = "instance.no_hostname", message = "La instance no tiene hostname (CustomDomain/AutoHostname) ni se pasó 'hostname' en el body." });
            }

            var baseEnv = await envResolver.ResolveRuntimeEnvAsync(
                new EnvironmentScopeChain(instance.ProjectId, instance.TemplateId, instance.ClientId, instance.InstanceId), ct);

            var results = new List<object>();
            foreach (var svc in services)
            {
                var containerName = $"{instance.Slug}-{svc.Name}";
                var env = new Dictionary<string, string>(baseEnv);
                foreach (var kv in svc.Env)
                {
                    env[kv.Key] = kv.Value.Replace("{instance}", instance.Slug, StringComparison.Ordinal);
                }

                // Liberar nombre estable (redeploy) — best-effort.
                await satellite.SendRemoveAsync(instance.TargetVmId, containerName, force: true, ct);

                var spec = new RunSpec(
                    ContainerName: containerName,
                    ImageRef: svc.Image,
                    Env: env,
                    Ports: [new PortBinding(svc.Port, null, "tcp")],
                    Volumes: [],
                    Command: null,
                    Healthcheck: null,
                    NetworkName: appNetwork,
                    RestartPolicy: "unless-stopped");

                var run = await satellite.SendRunAsync(instance.TargetVmId, spec, pullFrom: null, ct);
                if (!run.Success)
                {
                    log.LogError("deploy-native: servicio {Svc} falló: {Err}", svc.Name, run.ErrorMessage);
                    return Results.Problem($"Servicio '{svc.Name}' no arrancó: {run.ErrorMessage}");
                }
                results.Add(new { service = svc.Name, container = containerName, containerId = run.ContainerId, image = svc.Image });
            }

            // Healthcheck simple: esperar a que todos los contenedores estén "Up".
            var names = services.Select(s => $"{instance.Slug}-{s.Name}").ToHashSet(StringComparer.Ordinal);
            var healthy = false;
            for (var attempt = 1; attempt <= 15 && !healthy; attempt++)
            {
                await Task.Delay(2000, ct);
                var containers = await satellite.SendListContainersAsync(instance.TargetVmId, ct);
                healthy = names.All(nm => containers.Any(c =>
                    string.Equals(c.Name, nm, StringComparison.Ordinal)
                    && c.Status.StartsWith("Up", StringComparison.OrdinalIgnoreCase)));
            }

            // Rutas YARP: una por (servicio, pathPrefix). El más específico gana (F-proxy path-routing).
            var routes = new List<string>();
            foreach (var svc in services)
            {
                foreach (var prefix in svc.PathPrefixes)
                {
                    var backend = $"http://{instance.Slug}-{svc.Name}:{svc.Port}";
                    var r = await mediator.Send(new CreateRouteCommand(hostname!, backend, false, prefix), ct);
                    routes.Add(r.IsSuccess ? $"{prefix} → {backend}" : $"{prefix} (ya existía / {r.Error.Code})");
                }
            }

            // Monitor del hostname (idempotente: si el slug ya existe, se ignora el conflicto).
            var monitorSlug = $"{instance.Slug}";
            var mon = await mediator.Send(new CreateMonitorCommand(
                Slug: monitorSlug,
                Name: instance.Slug,
                Url: $"https://{hostname}/",
                HttpMethod: "GET",
                ExpectedStatusCodes: [200, 301, 302, 307, 308],
                IntervalSec: 120,
                TimeoutMs: 15000,
                Headers: null,
                BodyTemplate: null,
                InstanceId: instance.InstanceId,
                ProjectId: instance.ProjectId), ct);

            return Results.Ok(new
            {
                instanceId = instance.InstanceId,
                hostname,
                network = appNetwork,
                healthy,
                services = results,
                routes,
                monitor = mon.IsSuccess ? monitorSlug : $"(ya existía / {mon.Error.Code})",
            });
        })
        .RequireAuthorization("scope:projects:write")
        .WithName("DeployInstanceNative")
        .WithTags("Deployments");

        return app;
    }

    public sealed record DeployNativeRequest(string? Hostname);
}
