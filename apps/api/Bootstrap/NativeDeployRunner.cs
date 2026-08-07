using Aethra.Modules.Cloudflare.UseCases.DnsRecords.Commands;
using Aethra.Modules.Cloudflare.UseCases.DnsRecords.Queries;
using Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands;
using Aethra.Modules.Cloudflare.UseCases.Zones.Queries;
using Aethra.Modules.Deployments.Domain.Deployment;
using Aethra.Modules.Deployments.Infrastructure;
using Aethra.Modules.Deployments.Infrastructure.Build;
using Aethra.Modules.Monitoring.UseCases.Commands;
using Aethra.Modules.Proxy.UseCases.Routes;
using Aethra.Modules.Proxy.UseCases.Routes.Commands;
using Aethra.Modules.Proxy.UseCases.Routes.Queries;
using Aethra.Shared.Contracts.Containers;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Contracts.Settings;
using Aethra.Shared.Kernel.Time;
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

public sealed record NativeRestartResult(
    bool Success,
    string? Error,
    IReadOnlyList<string> Services);

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
    IIntegrationCredentialResolver credentialResolver,
    IMediator mediator,
    DeploymentsDbContext deploymentsDb,
    IClock clock,
    IConfiguration config,
    ILogger<NativeDeployRunner> log)
{
    private readonly string _appNetwork =
        config["Deployments:AppNetwork"] is { Length: > 0 } n ? n : "aethra-net";

    // F13.5 — target del CNAME del túnel CF (ej. "<uuid>.cfargotunnel.com"). Si está configurado,
    // el deploy crea automáticamente el DNS record del hostname (best-effort). Null = no auto-DNS.
    private readonly string? _tunnelCname = config["NativeDeploy:TunnelCname"];

    public async Task<NativeDeployResult> DeployAsync(
        string instanceId,
        string? hostnameOverride,
        CancellationToken ct,
        string? serviceName = null)
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
        // Set COMPLETO de servicios del template (antes de filtrar por serviceName). La limpieza de
        // contenedores zombi se basa en ÉSTE, no en el subconjunto filtrado: un deploy incremental de
        // un solo servicio no debe borrar los contenedores de los demás servicios legítimos.
        var allTemplateServices = services;
        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            services = services
                .Where(s => string.Equals(s.Name, serviceName.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (services.Count == 0)
            {
                return Fail($"El template no define un servicio llamado '{serviceName}'.");
            }
        }
        // Hostname para (re)crear rutas + monitor. En un REDEPLOY (webhook) puede no haber: los
        // contenedores tienen nombre estable {slug}-{service}, así que las rutas existentes siguen
        // sirviendo y solo refrescamos contenedores (se omiten rutas/monitor).
        var hostname = hostnameOverride ?? instance.CustomDomain ?? instance.AutoHostname;

        var deployment = Deployment.Queue(
            buildId: $"native:{Guid.NewGuid():N}"[..39],
            instanceId: instance.InstanceId,
            newImageRef: string.IsNullOrWhiteSpace(serviceName)
                ? $"native/{instance.Slug}:multi-service"
                : $"native/{instance.Slug}:{serviceName.Trim()}",
            trigger: DeploymentTrigger.Manual,
            triggeredBy: "deploy-native",
            now: clock.UtcNow);
        deployment.AppendLog(DeploymentLogLevel.Info, "pending",
            string.IsNullOrWhiteSpace(serviceName)
                ? $"Deploy nativo iniciado para {services.Count} servicio(s)."
                : $"Deploy nativo incremental iniciado para servicio '{serviceName.Trim()}'.",
            clock.UtcNow);
        deploymentsDb.Deployments.Add(deployment);
        await deploymentsDb.SaveChangesAsync(ct).ConfigureAwait(false);

        try
        {
            deployment.Transition(DeploymentStatus.Pulling, clock.UtcNow);
            await deploymentsDb.SaveChangesAsync(ct).ConfigureAwait(false);

            var baseEnv = await envResolver.ResolveRuntimeEnvAsync(
                new EnvironmentScopeChain(instance.ProjectId, instance.TemplateId, instance.ClientId, instance.InstanceId), ct)
                .ConfigureAwait(false);

        // Modo git: clonar el repo UNA vez (al branch trackeado) y construir cada imagen.
        BuildContextResult? gitCtx = null;
        string? shortSha = null;
        if (services.Any(s => string.Equals(s.BuildMode, "git", StringComparison.OrdinalIgnoreCase)))
        {
            var branch = !string.IsNullOrWhiteSpace(instance.TrackedRef) ? instance.TrackedRef! : template.Branch;
            // Repo privado: resolvemos el token de la credencial del template (si la hay) para que
            // el clone autentique. El secreto nunca se loguea (solo viaja en los args de git).
            var cloneToken = string.IsNullOrWhiteSpace(template.AccessTokenCredentialName)
                ? null
                : await credentialResolver.GetSecretAsync(template.AccessTokenCredentialName!, ct).ConfigureAwait(false);
            gitCtx = await buildContext.BuildAsync(template.GitRepoUrl, branch, null, template.BaseDirectory ?? string.Empty, cloneToken, ct)
                .ConfigureAwait(false);
            shortSha = gitCtx.ResolvedSha.Length >= 7 ? gitCtx.ResolvedSha[..7] : gitCtx.ResolvedSha;
            log.LogInformation("native-deploy {Inst}: contexto git {Repo}@{Branch} → {Sha}", instance.Slug, template.GitRepoUrl, branch, shortSha);
        }

        deployment.Transition(DeploymentStatus.Starting, clock.UtcNow);
        await deploymentsDb.SaveChangesAsync(ct).ConfigureAwait(false);

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
                    Mode: BuildMode.Dockerfile,
                    BuildContextDir: svc.BuildContext);
                var br = await satellite.SendBuildAsync(instance.TargetVmId, buildSpec, pushTo: null, ct).ConfigureAwait(false);
                foreach (var line in br.LogLines)
                {
                    deployment.AppendLog(DeploymentLogLevel.Info, "starting", line, clock.UtcNow);
                }
                await deploymentsDb.SaveChangesAsync(ct).ConfigureAwait(false);
                if (!br.Success)
                {
                    await FailNativeDeploymentAsync(deployment, "build_failed",
                        $"Build git del servicio '{svc.Name}' fallo: {br.ErrorMessage}", ct).ConfigureAwait(false);
                }
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
                await FailNativeDeploymentAsync(deployment, "run_failed",
                    $"Servicio '{svc.Name}' no arranco: {run.ErrorMessage}", ct).ConfigureAwait(false);
            }
            if (!run.Success)
            {
                return Fail($"Servicio '{svc.Name}' no arrancó: {run.ErrorMessage}", hostname);
            }
            deployedServices.Add($"{svc.Name} ({svc.BuildMode}) → {image}");
        }

        // Healthcheck: todos los contenedores Up.
        var names = services.Select(s => $"{instance.Slug}-{s.Name}").ToHashSet(StringComparer.Ordinal);
        deployment.RecordNewContainer(names.First(), clock.UtcNow);
        deployment.Transition(DeploymentStatus.Healthcheck, clock.UtcNow);
        await deploymentsDb.SaveChangesAsync(ct).ConfigureAwait(false);
        var healthy = false;
        for (var attempt = 1; attempt <= 15 && !healthy; attempt++)
        {
            await Task.Delay(2000, ct).ConfigureAwait(false);
            var containers = await satellite.SendListContainersAsync(instance.TargetVmId, ct).ConfigureAwait(false);
            healthy = names.All(nm => containers.Any(c =>
                string.Equals(c.Name, nm, StringComparison.Ordinal)
                && c.Status.StartsWith("Up", StringComparison.OrdinalIgnoreCase)));
        }
        if (!healthy)
        {
            await FailNativeDeploymentAsync(deployment, "healthcheck_failed",
                "Uno o mas servicios no alcanzaron estado Up dentro del tiempo de espera.", ct).ConfigureAwait(false);
            return new NativeDeployResult(false,
                "Uno o mas servicios no alcanzaron estado Up dentro del tiempo de espera.",
                hostname, false, deployedServices, []);
        }

        deployment.Transition(DeploymentStatus.Swapping, clock.UtcNow);
        await deploymentsDb.SaveChangesAsync(ct).ConfigureAwait(false);

        var routes = new List<string>();
        var anyServiceHost = services.Any(s => !string.IsNullOrWhiteSpace(s.Hostname));
        if (!string.IsNullOrWhiteSpace(hostname) || anyServiceHost)
        {
            routes = await ReconcileRoutingAsync(
                instance.Slug, instance.InstanceId, instance.ProjectId, services, hostname, ct).ConfigureAwait(false);
        }
        else
        {
            log.LogInformation("native-deploy {Inst}: sin hostname → redeploy de contenedores (rutas/monitor existentes intactos)", instance.Slug);
        }

        // Limpieza post-deploy de contenedores zombi/vestigiales (problema #1: split-brain). Sólo
        // tras un deploy sano: los nuevos targets ya están Up y sirviendo. Elimina cualquier
        // {slug}-* que NO sea target del template (ej. {slug}-api-bgbak/-bak/-new/-qqi…, o un
        // servicio retirado) — corren TODOS los hosted services contra el Postgres compartido pero
        // sin las conexiones SignalR del PoS → reclaman docs y los expiran. Best-effort.
        var cleaned = await CleanupStaleContainersAsync(
            instance.TargetVmId, instance.Slug, allTemplateServices, ct).ConfigureAwait(false);
        foreach (var name in cleaned)
        {
            deployment.AppendLog(DeploymentLogLevel.Info, "swapping",
                $"Contenedor obsoleto/zombi eliminado: {name}", clock.UtcNow);
        }

        deployment.Complete(clock.UtcNow);
        await deploymentsDb.SaveChangesAsync(ct).ConfigureAwait(false);

        log.LogInformation("native-deploy {Inst} OK (healthy={H}, {N} servicios)", instance.Slug, healthy, deployedServices.Count);
        return new NativeDeployResult(true, null, hostname, healthy, deployedServices, routes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "native-deploy {Inst} fallo", instance.Slug);
            await FailNativeDeploymentAsync(deployment, "internal_error", ex.Message, ct).ConfigureAwait(false);
            return Fail(ex.Message, hostname);
        }
    }

    public async Task<NativeRestartResult> RestartAsync(string instanceId, CancellationToken ct)
    {
        var instance = await instanceLookup.GetByIdAsync(instanceId, ct).ConfigureAwait(false);
        if (instance is null)
        {
            return RestartFail($"Instance '{instanceId}' no existe.");
        }
        var template = await templateLookup.GetByIdAsync(instance.TemplateId, ct).ConfigureAwait(false);
        if (template is null)
        {
            return RestartFail("Template de la instance no existe.");
        }
        var services = template.Services ?? [];
        if (services.Count == 0)
        {
            return RestartFail("El template no define servicios (Services).");
        }

        var restarted = new List<string>();
        foreach (var svc in services)
        {
            var containerName = $"{instance.Slug}-{svc.Name}";
            await satellite.SendRestartAsync(instance.TargetVmId, containerName, ct).ConfigureAwait(false);
            restarted.Add(containerName);
        }

        log.LogInformation("native-restart {Inst} OK ({N} servicios)", instance.Slug, restarted.Count);
        return new NativeRestartResult(true, null, restarted);
    }

    /// <summary>
    /// Problema #1 (split-brain) — elimina los contenedores <c>{slug}-*</c> de esta Instance que NO
    /// son target de NINGÚN servicio del template. El deploy nombra sus targets de forma estable
    /// (<c>{slug}-{service}</c>) y recrea cada uno (remove + run); pero los blue-green/renames manuales
    /// (<c>{slug}-api-bgbak</c>, <c>-bak</c>, <c>-new</c>, sufijos <c>-qqi…</c>) o un servicio retirado
    /// del template dejan contenedores viejos corriendo. Si los hosted services de la app desplegada
    /// no son leader-only y su SignalR no tiene backplane, ese duplicado corre TODOS sus jobs contra
    /// el Postgres compartido pero SIN las conexiones SignalR de los clientes → reclama trabajo que
    /// no puede completar, lo deja colgado en su estado intermedio y acaba expirándolo.
    /// Es un modo de fallo observado en producción, no teórico: el contenedor huérfano parece inocuo
    /// (no recibe tráfico del proxy) y sin embargo compite por la cola de trabajo de la app viva.
    /// Por eso la limpieza usa el set COMPLETO de servicios del template (no el subconjunto de un
    /// deploy incremental). Best-effort e idempotente: cualquier fallo se loguea y no rompe el deploy.
    /// </summary>
    private async Task<IReadOnlyList<string>> CleanupStaleContainersAsync(
        string vmId, string slug, IReadOnlyList<TemplateServiceView> allServices, CancellationToken ct)
    {
        var removed = new List<string>();
        try
        {
            var legit = allServices
                .Select(s => $"{slug}-{s.Name}")
                .ToHashSet(StringComparer.Ordinal);
            // El separador "-" evita que un slug que es prefijo de otro (p.ej. "app" vs "app2") se
            // confunda: "app2-api" no empieza por "app-".
            var prefix = $"{slug}-";

            var containers = await satellite.SendListContainersAsync(vmId, ct).ConfigureAwait(false);
            foreach (var c in containers)
            {
                if (!c.Name.StartsWith(prefix, StringComparison.Ordinal) || legit.Contains(c.Name))
                {
                    continue;
                }
                try
                {
                    await satellite.SendRemoveAsync(vmId, c.Name, force: true, ct).ConfigureAwait(false);
                    removed.Add(c.Name);
                    log.LogWarning(
                        "native-deploy {Slug}: contenedor obsoleto/zombi eliminado: {Name} (img {Image}, estado {Status})",
                        slug, c.Name, c.Image, c.Status);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning(ex, "native-deploy {Slug}: no se pudo eliminar contenedor obsoleto {Name}", slug, c.Name);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "native-deploy {Slug}: limpieza de contenedores obsoletos falló (ignorada)", slug);
        }
        return removed;
    }

    /// <summary>
    /// F13.8 — reconcilia SOLO el routing de una Instance nativa hacia su hostname deseado actual
    /// (<c>CustomDomain ?? AutoHostname</c>): crea rutas del host nuevo, borra las rutas viejas que
    /// apuntaban a esta Instance bajo otro host, refresca CNAME y monitor. NO recrea contenedores.
    /// La usa el handler de cambio de dominio para "personalizar la URL" dejando todo limpio.
    /// </summary>
    public async Task ReconcileRoutingForInstanceAsync(string instanceId, CancellationToken ct)
    {
        var instance = await instanceLookup.GetByIdAsync(instanceId, ct).ConfigureAwait(false);
        if (instance is null)
        {
            return;
        }
        var template = await templateLookup.GetByIdAsync(instance.TemplateId, ct).ConfigureAwait(false);
        var services = template?.Services ?? [];
        if (services.Count == 0)
        {
            return; // solo aplica a instancias nativas multi-servicio
        }
        var hostname = instance.CustomDomain ?? instance.AutoHostname;
        var anyServiceHost = services.Any(s => !string.IsNullOrWhiteSpace(s.Hostname));
        if (string.IsNullOrWhiteSpace(hostname) && !anyServiceHost)
        {
            return;
        }
        await ReconcileRoutingAsync(instance.Slug, instance.InstanceId, instance.ProjectId, services, hostname, ct)
            .ConfigureAwait(false);
        log.LogInformation("reconcile-routing {Inst}: rutas/CNAME/monitor sincronizados (multi-host)", instance.Slug);
    }

    /// <summary>
    /// Asegura las rutas del <paramref name="hostname"/> deseado (una por servicio×pathPrefix →
    /// <c>{slug}-{svc}:{port}</c>), BORRA las rutas que apuntan a contenedores de esta Instance pero
    /// bajo OTRO hostname (limpieza de URL anterior), y refresca CNAME + monitor. Crea-antes-de-borrar
    /// para no dejar la URL caída en la transición.
    /// </summary>
    private async Task<List<string>> ReconcileRoutingAsync(
        string slug, string instanceId, string projectId,
        IReadOnlyList<TemplateServiceView> services, string? instanceHostname, CancellationToken ct)
    {
        var routes = new List<string>();
        // Set deseado de (host, pathPrefix) para limpiar SOLO rutas mías realmente obsoletas.
        var desired = new HashSet<(string Host, string Prefix)>();
        var hostsTouched = new List<string>();

        // 1) Asegurar rutas: cada servicio bajo su Hostname propio (multi-host) o el de la Instance.
        // Un servicio con Hostname propio pero sin PathPrefixes asume "/" (quiere ruta). Sin host
        // ni prefix → servicio interno, sin ruta pública.
        foreach (var svc in services)
        {
            var host = !string.IsNullOrWhiteSpace(svc.Hostname) ? svc.Hostname! : instanceHostname;
            if (string.IsNullOrWhiteSpace(host))
            {
                continue;
            }
            IReadOnlyList<string> prefixes = svc.PathPrefixes.Count > 0
                ? svc.PathPrefixes
                : (!string.IsNullOrWhiteSpace(svc.Hostname) ? new[] { "/" } : svc.PathPrefixes);
            if (prefixes.Count == 0)
            {
                continue;
            }
            if (!hostsTouched.Contains(host, StringComparer.OrdinalIgnoreCase))
            {
                hostsTouched.Add(host);
            }
            foreach (var prefix in prefixes)
            {
                var backend = $"http://{slug}-{svc.Name}:{svc.Port}";
                var r = await mediator.Send(new CreateRouteCommand(
                    host, backend, false, prefix,
                    "app_environment", instanceId, RouteOwnershipRules.NativeDeployOrigin), ct).ConfigureAwait(false);
                routes.Add(r.IsSuccess ? $"{host}{prefix} → {backend}" : $"{host}{prefix} (ya existía)");
                desired.Add((host.ToLowerInvariant(), prefix));
            }
        }

        // 2) CNAME + tunnel ingress por cada host tocado (best-effort, idempotente).
        foreach (var host in hostsTouched)
        {
            await EnsureDnsRecordAsync(host, ct).ConfigureAwait(false);
        }

        // 3) Limpiar SOLO rutas mías ({slug}-{svc}: + Origin propio) que NO estén en el set deseado
        // (host+prefix). Borra hosts viejos de ESTA Instance sin tocar las rutas multi-host vigentes
        // ni las que apuntan al mismo backend pero no creamos nosotros (ver OT-001, RouteOwnershipRules).
        var myBackends = services.Select(s => $"http://{slug}-{s.Name}:").ToList();
        var all = await mediator.Send(new ListRoutesQuery(), ct).ConfigureAwait(false);
        if (all.IsSuccess)
        {
            foreach (var rt in all.Value)
            {
                if (RouteOwnershipRules.IsObsoleteOwnRoute(rt, myBackends, desired))
                {
                    await mediator.Send(new DeleteRouteCommand(rt.Id), ct).ConfigureAwait(false);
                    routes.Add($"− {rt.Hostname}{rt.PathPrefix} (obsoleta, borrada)");
                    log.LogInformation("reconcile-routing {Slug}: ruta obsoleta borrada {Host}{Path}", slug, rt.Hostname, rt.PathPrefix);
                }
            }
        }

        // 4) Monitor por cada host tocado (slug propio cuando hay varios hosts).
        foreach (var host in hostsTouched)
        {
            var monSlug = hostsTouched.Count > 1 ? host.Split('.')[0] : slug;
            await mediator.Send(new CreateMonitorCommand(
                Slug: monSlug, Name: host, Url: $"https://{host}/",
                HttpMethod: "GET", ExpectedStatusCodes: [200, 301, 302, 307, 308],
                IntervalSec: 120, TimeoutMs: 15000, Headers: null, BodyTemplate: null,
                InstanceId: instanceId, ProjectId: projectId), ct).ConfigureAwait(false);
        }

        return routes;
    }

    /// <summary>
    /// F13.5 — crea el DNS record (CNAME proxied → túnel CF) del hostname si hay <c>NativeDeploy:TunnelCname</c>
    /// configurado y una zona registrada que lo cubra. Best-effort: cualquier fallo se loguea y se ignora
    /// (el deploy no depende de esto; el ingress de cloudflared sigue siendo manual).
    /// </summary>
    private async Task EnsureDnsRecordAsync(string hostname, CancellationToken ct)
    {
        // F13.9 — si hay un Tunnel gestionado remoto, asegura la regla de ingress del host (cero blip).
        // No-op si no hay túnel registrado. Best-effort: no rompe el deploy.
        try
        {
            var ing = await mediator.Send(new EnsureTunnelHostnameCommand(hostname), ct).ConfigureAwait(false);
            if (ing.IsSuccess)
            {
                log.LogInformation("tunnel-ingress {Host}: regla asegurada (o ya existía).", hostname);
            }
            else
            {
                log.LogWarning("tunnel-ingress {Host}: {Err}", hostname, ing.Error.Message);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "tunnel-ingress {Host}: error (ignorado).", hostname);
        }

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

    private static NativeRestartResult RestartFail(string error)
        => new(false, error, []);

    private async Task FailNativeDeploymentAsync(
        Deployment deployment,
        string code,
        string message,
        CancellationToken ct)
    {
        deployment.Fail(code, message, clock.UtcNow);
        await deploymentsDb.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
