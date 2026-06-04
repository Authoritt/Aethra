using Aethra.Modules.Cloudflare.UseCases.DnsRecords.Commands;
using Aethra.Modules.Cloudflare.UseCases.DnsRecords.Queries;
using Aethra.Modules.Cloudflare.UseCases.Zones.Queries;
using Aethra.Modules.Deployments.Infrastructure.Build;
using Aethra.Modules.Monitoring.UseCases.Commands;
using Aethra.Modules.Proxy.UseCases.Routes.Commands;
using Aethra.Shared.Contracts.Containers;
using Aethra.Shared.Contracts.Projects;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aethra.Api.Bootstrap;

public sealed record NativeDeployResult(
    bool Success,
    string? Error,
    string? Hostname,
    bool Healthy,
    IReadOnlyList<string> Services,
    IReadOnlyList<string> Routes);

/// <summary>
/// F13 — orquestación reutilizable del deploy nativo multi-contenedor de una Instance.
/// La usan tanto el endpoint manual (<c>POST /api/instances/{id}/deploy-native</c>) como el
/// auto-trigger por webhook (push → redeploy de las instancias que trackean la rama).
/// Por cada servicio del template: resuelve la imagen (modo git → build en satélite; registry →
/// imagen prebuilt), corre el contenedor en <c>aethra-net</c>, healthcheck, y crea rutas + monitor.
/// </summary>
public sealed class NativeDeployRunner(
    IInstanceLookup instanceLookup,
    ITemplateLookup templateLookup,
    IEnvironmentResolver envResolver,
    ISatelliteRpcClient satellite,
    IBuildContextBuilder buildContext,
    IMediator mediator,
    IConfiguration config,
    ILogger<NativeDeployRunner> log)
{
    private readonly string _appNetwork =
        config["Deployments:AppNetwork"] is { Length: > 0 } n ? n : "aethra-net";

    // F13.5 — target del CNAME del túnel CF (ej. "<uuid>.cfargotunnel.com"). Si está configurado,
    // el deploy crea automáticamente el DNS record del hostname (best-effort). Null = no auto-DNS.
    private readonly string? _tunnelCname = config["NativeDeploy:TunnelCname"];

    public async Task<NativeDeployResult> DeployAsync(string instanceId, string? hostnameOverride, CancellationToken ct)
    {
        var instance = await instanceLookup.GetByIdAsync(instanceId, ct).ConfigureAwait(false);
        if (instance is null)
        {
            return Fail($"Instance '{instanceId}' no existe.");
        }
        var template = await templateLookup.GetByIdAsync(instance.TemplateId, ct).ConfigureAwait(false);
        if (template is null)
        {
            return Fail("Template de la instance no existe.");
        }
        var services = template.Services ?? [];
        if (services.Count == 0)
        {
            return Fail("El template no define servicios (Services).");
        }
        // Hostname para (re)crear rutas + monitor. En un REDEPLOY (webhook) puede no haber: los
        // contenedores tienen nombre estable {slug}-{service}, así que las rutas existentes siguen
        // sirviendo y solo refrescamos contenedores (se omiten rutas/monitor).
        var hostname = hostnameOverride ?? instance.CustomDomain ?? instance.AutoHostname;

        var baseEnv = await envResolver.ResolveRuntimeEnvAsync(
            new EnvironmentScopeChain(instance.ProjectId, instance.TemplateId, instance.ClientId, instance.InstanceId), ct)
            .ConfigureAwait(false);

        // Modo git: clonar el repo UNA vez (al branch trackeado) y construir cada imagen.
        BuildContextResult? gitCtx = null;
        string? shortSha = null;
        if (services.Any(s => string.Equals(s.BuildMode, "git", StringComparison.OrdinalIgnoreCase)))
        {
            var branch = !string.IsNullOrWhiteSpace(instance.TrackedRef) ? instance.TrackedRef! : template.Branch;
            gitCtx = await buildContext.BuildAsync(template.GitRepoUrl, branch, null, template.BaseDirectory ?? string.Empty, ct)
                .ConfigureAwait(false);
            shortSha = gitCtx.ResolvedSha.Length >= 7 ? gitCtx.ResolvedSha[..7] : gitCtx.ResolvedSha;
            log.LogInformation("native-deploy {Inst}: contexto git {Repo}@{Branch} → {Sha}", instance.Slug, template.GitRepoUrl, branch, shortSha);
        }

        var deployedServices = new List<string>();
        foreach (var svc in services)
        {
            var containerName = $"{instance.Slug}-{svc.Name}";
            var env = new Dictionary<string, string>(baseEnv);
            foreach (var kv in svc.Env)
            {
                env[kv.Key] = kv.Value.Replace("{instance}", instance.Slug, StringComparison.Ordinal);
            }

            string image;
            if (string.Equals(svc.BuildMode, "git", StringComparison.OrdinalIgnoreCase))
            {
                image = $"aethra/{instance.Slug}-{svc.Name}:{shortSha}";
                var buildSpec = new BuildSpec(
                    ImageRef: image,
                    BuildContextTarGz: gitCtx!.TarGz,
                    DockerfilePath: string.IsNullOrWhiteSpace(svc.DockerfilePath) ? "Dockerfile" : svc.DockerfilePath!,
                    BuildArgs: new Dictionary<string, string>(),
                    BuildSecrets: null,
                    Mode: BuildMode.Dockerfile);
                var br = await satellite.SendBuildAsync(instance.TargetVmId, buildSpec, pushTo: null, ct).ConfigureAwait(false);
                if (!br.Success)
                {
                    return Fail($"Build (git) del servicio '{svc.Name}' falló: {br.ErrorMessage}", hostname);
                }
            }
            else
            {
                image = svc.Image;
            }

            try
            {
                await satellite.SendRemoveAsync(instance.TargetVmId, containerName, force: true, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogDebug("native-deploy: remove previo de {Name} ignorado: {Msg}", containerName, ex.Message);
            }

            // F13.3 — volúmenes persistentes (ej. DataProtection keys). El nombre admite {instance}
            // → slug, de modo que cada Instance del template tiene su propio named volume.
            var volumes = (svc.Volumes ?? [])
                .Select(v => new VolumeBinding(
                    v.Name.Replace("{instance}", instance.Slug, StringComparison.Ordinal),
                    v.ContainerPath,
                    v.ReadOnly))
                .ToList();

            var spec = new RunSpec(
                ContainerName: containerName,
                ImageRef: image,
                Env: env,
                Ports: [new PortBinding(svc.Port, null, "tcp")],
                Volumes: volumes,
                Command: null,
                Healthcheck: null,
                NetworkName: _appNetwork,
                RestartPolicy: "unless-stopped");

            var run = await satellite.SendRunAsync(instance.TargetVmId, spec, pullFrom: null, ct).ConfigureAwait(false);
            if (!run.Success)
            {
                return Fail($"Servicio '{svc.Name}' no arrancó: {run.ErrorMessage}", hostname);
            }
            deployedServices.Add($"{svc.Name} ({svc.BuildMode}) → {image}");
        }

        // Healthcheck: todos los contenedores Up.
        var names = services.Select(s => $"{instance.Slug}-{s.Name}").ToHashSet(StringComparer.Ordinal);
        var healthy = false;
        for (var attempt = 1; attempt <= 15 && !healthy; attempt++)
        {
            await Task.Delay(2000, ct).ConfigureAwait(false);
            var containers = await satellite.SendListContainersAsync(instance.TargetVmId, ct).ConfigureAwait(false);
            healthy = names.All(nm => containers.Any(c =>
                string.Equals(c.Name, nm, StringComparison.Ordinal)
                && c.Status.StartsWith("Up", StringComparison.OrdinalIgnoreCase)));
        }

        var routes = new List<string>();
        if (!string.IsNullOrWhiteSpace(hostname))
        {
            // F13.5 — auto-DNS: crea el CNAME del hostname → túnel CF (best-effort, nunca falla el deploy).
            await EnsureDnsRecordAsync(hostname!, ct).ConfigureAwait(false);

            foreach (var svc in services)
            {
                foreach (var prefix in svc.PathPrefixes)
                {
                    var backend = $"http://{instance.Slug}-{svc.Name}:{svc.Port}";
                    var r = await mediator.Send(new CreateRouteCommand(hostname!, backend, false, prefix), ct).ConfigureAwait(false);
                    routes.Add(r.IsSuccess ? $"{prefix} → {backend}" : $"{prefix} (ya existía)");
                }
            }

            await mediator.Send(new CreateMonitorCommand(
                Slug: instance.Slug, Name: instance.Slug, Url: $"https://{hostname}/",
                HttpMethod: "GET", ExpectedStatusCodes: [200, 301, 302, 307, 308],
                IntervalSec: 120, TimeoutMs: 15000, Headers: null, BodyTemplate: null,
                InstanceId: instance.InstanceId, ProjectId: instance.ProjectId), ct).ConfigureAwait(false);
        }
        else
        {
            log.LogInformation("native-deploy {Inst}: sin hostname → redeploy de contenedores (rutas/monitor existentes intactos)", instance.Slug);
        }

        log.LogInformation("native-deploy {Inst} OK (healthy={H}, {N} servicios)", instance.Slug, healthy, deployedServices.Count);
        return new NativeDeployResult(true, null, hostname, healthy, deployedServices, routes);
    }

    /// <summary>
    /// F13.5 — crea el DNS record (CNAME proxied → túnel CF) del hostname si hay <c>NativeDeploy:TunnelCname</c>
    /// configurado y una zona registrada que lo cubra. Best-effort: cualquier fallo se loguea y se ignora
    /// (el deploy no depende de esto; el ingress de cloudflared sigue siendo manual).
    /// </summary>
    private async Task EnsureDnsRecordAsync(string hostname, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_tunnelCname))
        {
            return;
        }
        try
        {
            var zones = await mediator.Send(new ListZonesQuery(), ct).ConfigureAwait(false);
            if (zones.IsFailure)
            {
                log.LogDebug("auto-dns {Host}: no se pudieron listar zonas: {Err}", hostname, zones.Error.Message);
                return;
            }
            // Zona cuyo Name es sufijo del hostname (longest-match), ej. "example.com".
            var zone = zones.Value
                .Where(z => hostname.Equals(z.Name, StringComparison.OrdinalIgnoreCase)
                    || hostname.EndsWith("." + z.Name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(z => z.Name.Length)
                .FirstOrDefault();
            if (zone is null)
            {
                log.LogInformation("auto-dns {Host}: ninguna zona CF registrada lo cubre; omitido.", hostname);
                return;
            }

            var existing = await mediator.Send(new ListDnsRecordsQuery(zone.Id), ct).ConfigureAwait(false);
            if (existing.IsSuccess
                && existing.Value.Any(r => string.Equals(r.Name, hostname, StringComparison.OrdinalIgnoreCase)))
            {
                log.LogDebug("auto-dns {Host}: ya existe un record; omitido.", hostname);
                return;
            }

            var created = await mediator.Send(new CreateDnsRecordCommand(
                ZoneId: zone.Id, Type: "CNAME", Name: hostname, Content: _tunnelCname!,
                Ttl: 1, Proxied: true, Comment: "aethra native-deploy auto-dns"), ct).ConfigureAwait(false);
            if (created.IsSuccess)
            {
                log.LogInformation("auto-dns {Host}: CNAME → {Target} creado.", hostname, _tunnelCname);
            }
            else
            {
                log.LogWarning("auto-dns {Host}: no se pudo crear el CNAME: {Err}", hostname, created.Error.Message);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "auto-dns {Host}: error inesperado (ignorado).", hostname);
        }
    }

    private static NativeDeployResult Fail(string error, string? hostname = null)
        => new(false, error, hostname, false, [], []);
}
