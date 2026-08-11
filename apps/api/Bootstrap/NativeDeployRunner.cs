using Aethra.Modules.Cloudflare.UseCases.DnsRecords.Commands;
using Aethra.Modules.Cloudflare.UseCases.DnsRecords.Queries;
using Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands;
using Aethra.Modules.Cloudflare.UseCases.Zones.Queries;
using Aethra.Modules.Deployments.Domain.Deployment;
using Aethra.Modules.Deployments.Infrastructure;
using Aethra.Modules.Deployments.Infrastructure.Build;
using Aethra.Modules.Deployments.Rollout;
using Aethra.Modules.Monitoring.UseCases.Commands;
using Aethra.Modules.Monitoring.UseCases.Queries;
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

/// <param name="Warnings">
/// OT-006 — hechos del rollout que NO tumban el deploy pero no pueden quedar mudos: monitores que no
/// se crearon (<c>#53</c>) y el detalle de cada restauración de contenedor (<c>#49</c>/<c>#50</c>),
/// con su éxito o su fallo. Van además al log persistido del <c>Deployment</c>.
/// </param>
public sealed record NativeDeployResult(
    bool Success,
    string? Error,
    string? Hostname,
    bool Healthy,
    IReadOnlyList<string> Services,
    IReadOnlyList<string> Routes,
    IReadOnlyList<string> Warnings);

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

        // OT-006 #49/#50 — estado del rollout. Se declara FUERA del try porque el manejador de
        // excepciones también tiene que poder restaurar lo que ya se había sustituido.
        // - replacements: servicios cuyo contenedor ya se destruyó, EN ORDEN de aplicación.
        // - specsByService: la spec con la que se levantó cada uno; el restore reusa la MISMA
        //   cambiando solo la imagen (mismo criterio que DeploymentOrchestrator.DoRollbackAsync).
        var replacements = new List<ServiceReplacement>();
        var specsByService = new Dictionary<string, RunSpec>(StringComparer.Ordinal);
        var warnings = new List<string>();
        // Pasa a true en cuanto el healthcheck da sano. A partir de ahí NADA revierte contenedores:
        // lo que falle después (rutas, monitor, limpieza, persistencia) no es motivo para tumbar una
        // revisión que ya está corriendo sana — sería provocar la caída que esta OT existe para evitar.
        var swapConfirmed = false;

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

        // OT-006 #49 — foto del runtime ANTES de tocar nada: de aquí sale la imagen del contenedor
        // previo de cada servicio, lo único que permite restaurarlo si el reemplazo no queda sano
        // (el remove es force:true y no deja rastro). Una sola llamada, no N: los contenedores de
        // esta Instance solo cambian por este mismo deploy.
        // La foto lleva su fiabilidad: si no se pudo tomar, DecideReplacement aborta ANTES de borrar.
        var snapshotBefore = await TakeContainerSnapshotAsync(instance.TargetVmId, ct).ConfigureAwait(false);

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
                    var buildError = $"Build (git) del servicio '{svc.Name}' falló: {br.ErrorMessage}";
                    // OT-006 #50 — este servicio aún no se tocó, pero los 1..k-1 ya fueron
                    // sustituidos: sin esto el despliegue quedaba a medias y sin recuperación.
                    // Restaurar va ANTES de persistir el fallo (G2 B2): un error de BD no puede
                    // decidir si producción se queda caída.
                    warnings.AddRange(await FailAndRollbackAsync(
                        deployment, instance.TargetVmId, "build_failed", buildError,
                        replacements, specsByService, ct).ConfigureAwait(false));
                    return Fail(buildError, hostname, warnings);
                }
            }
            else
            {
                image = svc.Image;
            }

            // F13.3 — volúmenes persistentes (ej. DataProtection keys). El nombre admite {instance}
            // → slug, de modo que cada Instance del template tiene su propio named volume.
            var volumes = (svc.Volumes ?? [])
                .Select(v => new VolumeBinding(
                    v.Name.Replace("{instance}", instance.Slug, StringComparison.Ordinal),
                    v.ContainerPath,
                    v.ReadOnly))
                .ToList();

            // OT-006 — la spec se arma ANTES del remove (es cálculo puro, sin I/O) para que el
            // restore pueda reusar EXACTAMENTE la misma spec cambiando solo la imagen.
            var spec = new RunSpec(
                ContainerName: containerName,
                ImageRef: image,
                Env: env,
                Ports: [new PortBinding(
                    svc.Port,
                    svc.HostPort,
                    "tcp",
                    svc.HostPort is null ? null : "0.0.0.0")],
                Volumes: volumes,
                Command: null,
                // Healthcheck del contenedor: null a propósito → se hereda el HEALTHCHECK de la
                // imagen (si lo declara) y ContainerHealthRules LEE su veredicto del estado que
                // reporta el runtime. Poder configurar uno por servicio exige un campo en
                // TemplateServiceView (Shared.Contracts + Modules.Projects): gap encolado en OT-006.
                Healthcheck: null,
                NetworkName: _appNetwork,
                RestartPolicy: "unless-stopped");
            specsByService[svc.Name] = spec;

            // OT-006 #49 — capturar la revisión previa ANTES del remove destructivo y anotarla como
            // reemplazo ANTES de ejecutarlo: desde esta línea el servicio ya no tiene contenedor
            // propio, así que entra en el plan de restauración pase lo que pase después.
            var decision = NativeRolloutPlanner.DecideReplacement(svc.Name, containerName, image, snapshotBefore);
            if (!decision.CanProceed)
            {
                // La foto del runtime no se pudo tomar: abortamos SIN destruir nada. Los servicios
                // 1..k-1 ya sustituidos sí se restauran, porque de ésos sí tenemos su revisión.
                var abortError = $"Deploy abortado antes de tocar '{svc.Name}': {decision.AbortReason}";
                warnings.AddRange(await FailAndRollbackAsync(
                    deployment, instance.TargetVmId, "runtime_snapshot_unavailable", abortError,
                    replacements, specsByService, ct).ConfigureAwait(false));
                return Fail(abortError, hostname, warnings);
            }
            var replacement = decision.Replacement!;
            replacements.Add(replacement);
            if (deployment.OldContainerId is null && replacement.PreviousContainerId is { } previousId)
            {
                // El agregado exige tener registrado el contenedor previo para admitir RolledBack.
                deployment.RecordOldContainer(previousId, replacement.PreviousImageRef ?? string.Empty, clock.UtcNow);
            }

            // El remove NO es opcional ni se puede posponer: Docker rechaza crear un contenedor con
            // un nombre ya tomado (aunque el viejo esté PARADO) y ISatelliteRpcClient no expone
            // rename, así que no hay forma de apartar al anterior. La seguridad no viene de
            // conservarlo vivo —eso sería el split-brain que CleanupStaleContainersAsync documenta—
            // sino de poder RESTAURARLO, que es lo que se capturó arriba.
            try
            {
                await satellite.SendRemoveAsync(instance.TargetVmId, containerName, force: true, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogDebug("native-deploy: remove previo de {Name} ignorado: {Msg}", containerName, ex.Message);
            }

            var run = await satellite.SendRunAsync(instance.TargetVmId, spec, pullFrom: null, ct).ConfigureAwait(false);
            if (!run.Success)
            {
                var runError = $"Servicio '{svc.Name}' no arrancó: {run.ErrorMessage}";
                // OT-006 #49/#50 — incluye a ESTE servicio (ya se le borró el contenedor) y a los
                // 1..k-1 previos.
                warnings.AddRange(await FailAndRollbackAsync(
                    deployment, instance.TargetVmId, "run_failed", runError,
                    replacements, specsByService, ct).ConfigureAwait(false));
                return Fail(runError, hostname, warnings);
            }
            deployedServices.Add($"{svc.Name} ({svc.BuildMode}) → {image}");
        }

        // Healthcheck. OT-006 #51 — "sano" ya no es `Status.StartsWith("Up")`: ese predicado daba
        // verdadero para "Up 2 minutes (unhealthy)" y para "Up 3 seconds (health: starting)", o sea
        // que un contenedor con el healthcheck FALLANDO sustituía a la revisión anterior. La
        // decisión vive ahora en ContainerHealthRules (función pura, testeada).
        var names = services.Select(s => $"{instance.Slug}-{s.Name}").ToList();
        deployment.RecordNewContainer(names[0], clock.UtcNow);
        deployment.Transition(DeploymentStatus.Healthcheck, clock.UtcNow);
        await deploymentsDb.SaveChangesAsync(ct).ConfigureAwait(false);
        var verdict = new RolloutHealthVerdict(false, ["healthcheck aún no ejecutado"]);
        for (var attempt = 1; attempt <= 15 && !verdict.AllHealthy; attempt++)
        {
            await Task.Delay(2000, ct).ConfigureAwait(false);
            var containers = await satellite.SendListContainersAsync(instance.TargetVmId, ct).ConfigureAwait(false);
            verdict = ContainerHealthRules.EvaluateAll(names, containers);
        }
        var healthy = verdict.AllHealthy;
        if (!healthy)
        {
            var healthError = "Uno o más servicios no quedaron sanos dentro del tiempo de espera: "
                + string.Join("; ", verdict.Blockers);
            // OT-006 #49 — el reemplazo no pasó el healthcheck: se retira y vuelve la revisión
            // anterior. Antes se devolvía Fail y el servicio quedaba SIN NADA corriendo.
            warnings.AddRange(await FailAndRollbackAsync(
                deployment, instance.TargetVmId, "healthcheck_failed", healthError,
                replacements, specsByService, ct).ConfigureAwait(false));
            return new NativeDeployResult(false, healthError, hostname, false, deployedServices, [], warnings);
        }
        swapConfirmed = true;

        deployment.Transition(DeploymentStatus.Swapping, clock.UtcNow);
        await deploymentsDb.SaveChangesAsync(ct).ConfigureAwait(false);

        var routes = new List<string>();
        var anyServiceHost = services.Any(s => !string.IsNullOrWhiteSpace(s.Hostname));
        if (!string.IsNullOrWhiteSpace(hostname) || anyServiceHost)
        {
            var routing = await ReconcileRoutingAsync(
                instance.Slug, instance.InstanceId, instance.ProjectId, services, hostname, ct).ConfigureAwait(false);
            routes = [.. routing.Routes];
            foreach (var w in routing.Warnings)
            {
                warnings.Add(w);
                deployment.AppendLog(DeploymentLogLevel.Warn, "swapping", w, clock.UtcNow);
            }
            if (routing.Error is { } routingError)
            {
                // OT-006 #52 — un fallo REAL de creación de ruta (distinto de "ya existía") deja la
                // URL sin servir y antes se reportaba como éxito. Se propaga: el Deployment queda
                // Failed y el resultado dice por qué.
                // Los contenedores NO se revierten: ya están sanos, y tumbar la revisión nueva por
                // un fallo del proxy provocaría justo la caída que esta OT existe para evitar — lo
                // que falta es el enrutado, no la app.
                await FailNativeDeploymentAsync(deployment, "route_failed", routingError, ct).ConfigureAwait(false);
                return new NativeDeployResult(false, routingError, hostname, true, deployedServices, routes, warnings);
            }
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

        log.LogInformation("native-deploy {Inst} OK (healthy={H}, {N} servicios, {W} avisos)",
            instance.Slug, healthy, deployedServices.Count, warnings.Count);
        return new NativeDeployResult(true, null, hostname, healthy, deployedServices, routes, warnings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "native-deploy {Inst} fallo", instance.Slug);
            if (!swapConfirmed)
            {
                // OT-006 #50 — una excepción a media tanda dejaba igual de huérfanos a los servicios
                // ya sustituidos que un Fail explícito. Restaurar primero, persistir una vez (G2 B2).
                warnings.AddRange(await FailAndRollbackAsync(
                    deployment, instance.TargetVmId, "internal_error", ex.Message,
                    replacements, specsByService, ct).ConfigureAwait(false));
            }
            else
            {
                await FailNativeDeploymentAsync(deployment, "internal_error", ex.Message, ct).ConfigureAwait(false);
                // La excepción ocurrió DESPUÉS de que el healthcheck diera sano (rutas, monitor,
                // limpieza, persistencia): los contenedores nuevos están corriendo y se quedan.
                var note = "el fallo ocurrió tras confirmar el healthcheck: los contenedores nuevos NO se revierten "
                    + "(la revisión desplegada está sana; lo que falló es posterior al swap).";
                warnings.Add(note);
                log.LogWarning("native-deploy {Inst}: {Warning}", instance.Slug, note);
            }
            return Fail(ex.Message, hostname, warnings);
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
        var routing = await ReconcileRoutingAsync(
            instance.Slug, instance.InstanceId, instance.ProjectId, services, hostname, ct).ConfigureAwait(false);
        foreach (var w in routing.Warnings)
        {
            log.LogWarning("reconcile-routing {Inst}: {Warning}", instance.Slug, w);
        }
        if (routing.Error is { } err)
        {
            // OT-006 #52 — este camino (cambio de dominio) tampoco puede tragarse un fallo de ruta.
            log.LogError("reconcile-routing {Inst}: {Error}", instance.Slug, err);
            return;
        }
        log.LogInformation("reconcile-routing {Inst}: rutas/CNAME/monitor sincronizados (multi-host)", instance.Slug);
    }

    /// <summary>
    /// Resultado de una reconciliación de routing. <paramref name="Error"/> no nulo = al menos una
    /// ruta NO se pudo crear por una causa distinta de "ya existía" (OT-006 <c>#52</c>);
    /// <paramref name="Warnings"/> recoge lo que no tumba el deploy pero no puede quedar mudo
    /// (monitores no creados, OT-006 <c>#53</c>).
    /// </summary>
    private sealed record RoutingOutcome(
        IReadOnlyList<string> Routes,
        IReadOnlyList<string> Warnings,
        string? Error);

    /// <summary>
    /// Asegura las rutas del <paramref name="instanceHostname"/> deseado (una por servicio×pathPrefix →
    /// <c>{slug}-{svc}:{port}</c>), BORRA las rutas que apuntan a contenedores de esta Instance pero
    /// bajo OTRO hostname (limpieza de URL anterior), y refresca CNAME + monitor. Crea-antes-de-borrar
    /// para no dejar la URL caída en la transición.
    /// </summary>
    private async Task<RoutingOutcome> ReconcileRoutingAsync(
        string slug, string instanceId, string projectId,
        IReadOnlyList<TemplateServiceView> services, string? instanceHostname, CancellationToken ct)
    {
        var routes = new List<string>();
        var warnings = new List<string>();
        string? error = null;
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
                // OT-006 #52 — antes CUALQUIER fallo se etiquetaba "(ya existía)". Solo el conflicto
                // de hostname lo es; el resto (backend inválido, hostname inválido, validación) deja
                // la URL sin servir y no puede reportarse como éxito.
                // G2 B4 — y ni siquiera ese conflicto basta: el handler lo devuelve ante CUALQUIER
                // ruta con ese (host, prefix), sin mirar el backend. "Ya existía" solo es benigno si
                // la ruta que está apunta a MI backend; si no, el host sirve otra app y reportarlo
                // como éxito es tráfico secuestrado en silencio.
                string? existingBackend = null;
                if (!r.IsSuccess
                    && string.Equals(r.Error.Code, DeploySideEffectRules.RouteAlreadyExistsCode, StringComparison.Ordinal))
                {
                    existingBackend = await FindExistingRouteBackendAsync(host, prefix, ct).ConfigureAwait(false);
                }
                var outcome = DeploySideEffectRules.ClassifyRoute(
                    r.IsSuccess, r.IsSuccess ? null : r.Error.Code, backend, existingBackend);
                if (outcome == SideEffectOutcome.Created)
                {
                    routes.Add($"{host}{prefix} → {backend}");
                }
                else if (outcome == SideEffectOutcome.AlreadyExists)
                {
                    routes.Add($"{host}{prefix} (ya existía)");
                }
                else
                {
                    var msg = $"ruta {host}{prefix} → {backend} NO se pudo crear: [{r.Error.Code}] {r.Error.Message}";
                    routes.Add($"{host}{prefix} FALLÓ [{r.Error.Code}]");
                    error ??= msg;
                    log.LogError("reconcile-routing {Slug}: {Error}", slug, msg);
                }
                // Se añade al set deseado incluso si falló: `desired` es lo que protege a una ruta de
                // ser borrada en el paso 3, y una ruta que queríamos crear nunca debe quedar expuesta
                // a la limpieza por el hecho de que su creación fallara.
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
        // OT-006 #52 — si alguna ruta deseada NO se pudo crear, el estado deseado está incompleto y
        // este paso es el ÚNICO destructivo del método: borrar rutas viejas apoyándose en un set
        // deseado incompleto puede dejar el host sin ninguna ruta viva. Se omite y se reporta.
        var myBackends = services.Select(s => $"http://{slug}-{s.Name}:").ToList();
        if (error is not null)
        {
            log.LogWarning(
                "reconcile-routing {Slug}: limpieza de rutas obsoletas OMITIDA porque una ruta deseada falló", slug);
        }
        else
        {
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
        }

        // 4) Monitor por cada host tocado (slug propio cuando hay varios hosts).
        foreach (var host in hostsTouched)
        {
            var monSlug = hostsTouched.Count > 1 ? host.Split('.')[0] : slug;
            var monitorUrl = $"https://{host}/";
            var mon = await mediator.Send(new CreateMonitorCommand(
                Slug: monSlug, Name: host, Url: monitorUrl,
                HttpMethod: "GET", ExpectedStatusCodes: [200, 301, 302, 307, 308],
                IntervalSec: 120, TimeoutMs: 15000, Headers: null, BodyTemplate: null,
                InstanceId: instanceId, ProjectId: projectId), ct).ConfigureAwait(false);
            // OT-006 #53 — antes el resultado ni se asignaba: un monitor que no se crea dejaba la
            // app sin vigilancia y era invisible. Los dos conflictos benignos son el caso normal del
            // redeploy; cualquier otro error sale como aviso del deploy y al log del Deployment.
            // G2 B5 — el conflicto de SLUG no prueba que ESTE host esté vigilado (solo que el nombre
            // está tomado). Si lo tiene el monitor de otra app, la mía se queda sin vigilancia en
            // silencio, que es justo lo que #53 quería cerrar.
            string? existingMonitorUrl = null;
            if (!mon.IsSuccess
                && string.Equals(mon.Error.Code, DeploySideEffectRules.MonitorSlugTakenCode, StringComparison.Ordinal))
            {
                existingMonitorUrl = await FindExistingMonitorUrlAsync(monSlug, ct).ConfigureAwait(false);
            }
            if (DeploySideEffectRules.ClassifyMonitor(
                    mon.IsSuccess, mon.IsSuccess ? null : mon.Error.Code, monitorUrl, existingMonitorUrl)
                == SideEffectOutcome.Failed)
            {
                var msg = $"monitor '{monSlug}' para {host} NO se creó: [{mon.Error.Code}] {mon.Error.Message} "
                    + "— la app queda desplegada pero SIN vigilancia de disponibilidad.";
                warnings.Add(msg);
                log.LogWarning("reconcile-routing {Slug}: {Warning}", slug, msg);
            }
        }

        return new RoutingOutcome(routes, warnings, error);
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

    /// <summary>
    /// OT-006 (G2 <c>B1</c>) — foto del runtime antes de la fase destructiva, con su fiabilidad
    /// explícita.
    ///
    /// <para>
    /// La versión anterior devolvía lista vacía cuando el listado fallaba, con el argumento de que
    /// "perder la foto degrada la capacidad de rollback, no la corrección del deploy". Era FALSO: el
    /// deploy seguía y borraba igualmente el contenedor previo con <c>force:true</c>, así que un
    /// timeout transitorio del satélite convertía el deploy con rollback en uno destructivo — el
    /// fallo exacto que <c>#49</c> dice cerrar. Ahora el fallo viaja en el resultado y
    /// <see cref="NativeRolloutPlanner.DecideReplacement"/> lo convierte en un aborto ANTES de
    /// destruir nada.
    /// </para>
    /// </summary>
    private async Task<ContainerSnapshot> TakeContainerSnapshotAsync(string vmId, CancellationToken ct)
    {
        try
        {
            return ContainerSnapshot.Taken(
                await satellite.SendListContainersAsync(vmId, ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex,
                "native-deploy: no se pudo listar contenedores de la VM {Vm}; el rollout se aborta sin tocar nada",
                vmId);
            return ContainerSnapshot.Unavailable();
        }
    }

    /// <summary>
    /// OT-006 — lista los contenedores de la VM tolerando el fallo. Solo para verificaciones NO
    /// destructivas (la salud tras un restore): aquí una lista vacía degrada la comprobación, no
    /// habilita ningún borrado. La fase destructiva usa <see cref="TakeContainerSnapshotAsync"/>.
    /// </summary>
    private async Task<IReadOnlyList<ContainerInfo>> ListContainersForCheckAsync(string vmId, CancellationToken ct)
    {
        try
        {
            return await satellite.SendListContainersAsync(vmId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "native-deploy: no se pudo verificar el estado de los contenedores de {Vm}", vmId);
            return [];
        }
    }

    /// <summary>
    /// OT-006 <c>#49</c>/<c>#50</c> — deshace los reemplazos ya aplicados cuando el rollout falla:
    /// por cada servicio ya sustituido retira el contenedor nuevo y vuelve a levantar la imagen
    /// previa con la MISMA spec. El plan (qué y en qué orden) lo decide
    /// <see cref="NativeRolloutPlanner.PlanRollback"/>; aquí solo se ejecuta el I/O.
    ///
    /// <para>
    /// Best-effort deliberado: un paso que falla no aborta los demás — dejar restaurados 3 de 4
    /// servicios es estrictamente mejor que dejar 0. Cada paso, salga bien o mal, queda en el log
    /// persistido del <c>Deployment</c> y en los avisos del resultado: un rollback silencioso sería
    /// el mismo modo de fallo que esta OT persigue.
    /// </para>
    ///
    /// <para>
    /// El agregado pasa a <c>RolledBack</c> solo si TODO lo que había que restaurar se restauró; si
    /// quedó algo a medias el deployment se queda en <c>Failed</c>, que es la verdad.
    /// </para>
    /// </summary>
    private async Task<RestoreOutcome> RestoreReplacementsAsync(
        string vmId,
        IReadOnlyList<ServiceReplacement> replacements,
        Dictionary<string, RunSpec> specsByService,
        CancellationToken ct)
    {
        var notes = new List<(DeploymentLogLevel Level, string Text)>();
        var plan = NativeRolloutPlanner.PlanRollback(replacements);
        if (plan.Count == 0)
        {
            return new RestoreOutcome(notes, 0, true);
        }

        var attempted = 0;
        var restored = 0;
        var restoredNames = new List<string>();
        foreach (var step in plan)
        {
            if (step.Action == RollbackAction.LeaveForDiagnosis)
            {
                notes.Add((DeploymentLogLevel.Warn,
                    $"rollback: '{step.ServiceName}' no tenía revisión previa (primer deploy); se deja {step.ContainerName} en su sitio para poder leer sus logs."));
                continue;
            }

            attempted++;
            if (!specsByService.TryGetValue(step.ServiceName, out var spec))
            {
                notes.Add((DeploymentLogLevel.Error,
                    $"rollback: no hay spec registrada para '{step.ServiceName}'; {step.ContainerName} NO se pudo restaurar."));
                continue;
            }

            try
            {
                // Retirar el contenedor nuevo libera el nombre y los puertos publicados; sin esto el
                // run de la imagen previa chocaría con el nombre ya tomado.
                await satellite.SendRemoveAsync(vmId, step.ContainerName, force: true, ct).ConfigureAwait(false);
                var restoreSpec = spec with { ImageRef = step.RestoreImageRef! };
                var run = await satellite.SendRunAsync(vmId, restoreSpec, pullFrom: null, ct).ConfigureAwait(false);
                if (run.Success)
                {
                    restored++;
                    restoredNames.Add(step.ContainerName);
                    notes.Add((DeploymentLogLevel.Warn,
                        $"rollback: '{step.ServiceName}' restaurado a la revisión previa {step.RestoreImageRef}."));
                    log.LogWarning("native-deploy rollback: {Container} restaurado a {Image}",
                        step.ContainerName, step.RestoreImageRef);
                }
                else
                {
                    notes.Add((DeploymentLogLevel.Error,
                        $"rollback: '{step.ServiceName}' NO se pudo restaurar a {step.RestoreImageRef}: {run.ErrorMessage}"));
                    log.LogError("native-deploy rollback: {Container} NO restaurado: {Err}",
                        step.ContainerName, run.ErrorMessage);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                notes.Add((DeploymentLogLevel.Error,
                    $"rollback: '{step.ServiceName}' lanzó excepción al restaurar: {ex.Message}"));
                log.LogError(ex, "native-deploy rollback: excepción restaurando {Container}", step.ContainerName);
            }
        }

        var complete = attempted > 0 && restored == attempted;

        // OT-006 (G2 B6) — verificar que lo restaurado quedó SANO. "El run devolvió Success" solo
        // dice que el runtime aceptó el arranque; si la imagen previa ya no levanta (volumen migrado,
        // env incompatible con la revisión vieja) el servicio sigue caído y el rollback seria un
        // falso verde. Presupuesto corto a propósito: esto corre en un camino que ya falló.
        if (restoredNames.Count > 0)
        {
            var verdict = await VerifyRestoredHealthAsync(vmId, restoredNames, ct).ConfigureAwait(false);
            if (verdict.AllHealthy)
            {
                notes.Add((DeploymentLogLevel.Warn,
                    $"rollback: revisión previa verificada sana ({restoredNames.Count} contenedor(es))."));
            }
            else
            {
                complete = false;
                notes.Add((DeploymentLogLevel.Error,
                    "rollback: la revisión previa restaurada NO quedó sana — el servicio sigue caído y necesita intervención: "
                    + string.Join("; ", verdict.Blockers)));
                log.LogError("native-deploy rollback: la revisión restaurada NO quedó sana: {Blockers}",
                    string.Join("; ", verdict.Blockers));
            }
        }

        return new RestoreOutcome(notes, attempted, complete);
    }

    /// <summary>
    /// OT-006 (G2 <c>B6</c>) — comprueba la salud de los contenedores restaurados con la MISMA regla
    /// que el healthcheck del rollout (<see cref="ContainerHealthRules"/>). Presupuesto reducido
    /// (5 × 2s) porque esto ocurre dentro de un camino de fallo: aquí interesa detectar un rollback
    /// que no sirvió, no esperar a un arranque lento.
    /// </summary>
    private async Task<RolloutHealthVerdict> VerifyRestoredHealthAsync(
        string vmId, IReadOnlyList<string> containerNames, CancellationToken ct)
    {
        var verdict = ContainerHealthRules.EvaluateAll(containerNames, []);
        for (var attempt = 1; attempt <= 5 && !verdict.AllHealthy; attempt++)
        {
            try
            {
                await Task.Delay(2000, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Camino de cancelación: no podemos esperar, pero sí mirar una vez más sin dormir.
                verdict = ContainerHealthRules.EvaluateAll(
                    containerNames,
                    await ListContainersForCheckAsync(vmId, CancellationToken.None).ConfigureAwait(false));
                return verdict;
            }
            verdict = ContainerHealthRules.EvaluateAll(
                containerNames, await ListContainersForCheckAsync(vmId, ct).ConfigureAwait(false));
        }
        return verdict;
    }

    /// <summary>
    /// OT-006 (G2 <c>B2</c>) — cierra un rollout fallido en el orden correcto:
    /// <list type="number">
    /// <item><b>Restaurar contenedores</b> (el trabajo que salva producción), sin tocar la BD.</item>
    /// <item>Mutar el agregado EN MEMORIA (<c>Fail</c>, bitácora, y <c>Rollback</c> si procede).</item>
    /// <item>Persistir UNA vez, al final, dentro de un <c>try</c>.</item>
    /// </list>
    ///
    /// <para>
    /// Antes se persistía el fallo ANTES de restaurar. Si ese <c>SaveChangesAsync</c> reventaba, la
    /// excepción salía del bloque, el <c>catch</c> externo volvía a intentar persistir, reventaba
    /// otra vez y salía de <c>DeployAsync</c>: <b>el rollback no llegaba a ejecutarse nunca</b>. Un
    /// problema de base de datos no puede decidir si producción se queda caída.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<string>> FailAndRollbackAsync(
        Deployment deployment,
        string vmId,
        string code,
        string error,
        IReadOnlyList<ServiceReplacement> replacements,
        Dictionary<string, RunSpec> specsByService,
        CancellationToken ct)
    {
        var outcome = await RestoreReplacementsAsync(vmId, replacements, specsByService, ct).ConfigureAwait(false);

        deployment.Fail(code, error, clock.UtcNow);
        foreach (var (level, text) in outcome.Notes)
        {
            deployment.AppendLog(level, "swapping", text, clock.UtcNow);
        }
        // El agregado solo admite Failed → RolledBack, y exige tener capturado el contenedor previo.
        if (outcome.Attempted > 0 && outcome.Complete
            && deployment.Status == DeploymentStatus.Failed
            && !string.IsNullOrWhiteSpace(deployment.OldContainerId))
        {
            deployment.Rollback(clock.UtcNow);
        }

        await PersistQuietlyAsync("cierre del rollout fallido", ct).ConfigureAwait(false);
        return [.. outcome.Notes.Select(n => n.Text)];
    }

    /// <summary>
    /// Persiste sin dejar que un fallo de BD derribe el camino de recuperación en curso. Se usa en
    /// los cierres de fallo: llegados ahí, lo importante ya se hizo sobre los contenedores y perder
    /// la bitácora es un daño menor que abortar el flujo (OT-006, G2 <c>B2</c>).
    /// </summary>
    private async Task PersistQuietlyAsync(string what, CancellationToken ct)
    {
        try
        {
            await deploymentsDb.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "native-deploy: no se pudo persistir {What}", what);
        }
    }

    private static NativeDeployResult Fail(
        string error, string? hostname = null, IReadOnlyList<string>? warnings = null)
        => new(false, error, hostname, false, [], [], warnings ?? []);

    private static NativeRestartResult RestartFail(string error)
        => new(false, error, []);

    private async Task FailNativeDeploymentAsync(
        Deployment deployment,
        string code,
        string message,
        CancellationToken ct)
    {
        deployment.Fail(code, message, clock.UtcNow);
        await PersistQuietlyAsync($"el fallo '{code}' del deployment", ct).ConfigureAwait(false);
    }

    /// <summary>
    /// G2 <c>B4</c> — backend de la ruta que YA ocupa ese <c>(host, prefix)</c>, para poder decidir
    /// si el conflicto <c>route.hostname_taken</c> es benigno ("la ruta ya apunta a mi backend") o
    /// un secuestro ("apunta a otra app"). Se consulta SOLO cuando hay conflicto: es el caso raro.
    ///
    /// <para>Devuelve <c>null</c> si no se puede averiguar, y el clasificador trata eso como fallo:
    /// no poder demostrar que la ruta es mía no es lo mismo que demostrar que lo es.</para>
    /// </summary>
    private async Task<string?> FindExistingRouteBackendAsync(string hostname, string pathPrefix, CancellationToken ct)
    {
        try
        {
            var all = await mediator.Send(new ListRoutesQuery(), ct).ConfigureAwait(false);
            if (!all.IsSuccess)
            {
                return null;
            }
            // Misma normalización que usa el handler para detectar el conflicto, o compararíamos
            // contra una clave distinta de la que provocó el choque.
            // Cualificado: el runner ya trabaja con los DTO de ruta, y un `using` del dominio
            // introduciría un `Route` ambiguo en este fichero.
            var normalized = Aethra.Modules.Proxy.Domain.Route.NormalizePathPrefix(pathPrefix);
            return all.Value.FirstOrDefault(rt =>
                string.Equals(rt.Hostname, hostname, StringComparison.OrdinalIgnoreCase)
                && string.Equals(rt.PathPrefix, normalized, StringComparison.Ordinal))?.BackendUrl;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex,
                "native-deploy: no se pudo consultar la ruta existente de {Host}{Prefix}; el conflicto se tratará como fallo",
                hostname, pathPrefix);
            return null;
        }
    }

    /// <summary>
    /// G2 <c>B5</c> — URL del monitor que ya usa ese slug, para distinguir "mi app ya está vigilada"
    /// de "el nombre lo tiene el monitor de otra app". <c>null</c> ⇒ el clasificador falla cerrado.
    /// </summary>
    private async Task<string?> FindExistingMonitorUrlAsync(string monitorSlug, CancellationToken ct)
    {
        try
        {
            var all = await mediator.Send(new ListMonitorsQuery(null, null, null, null), ct).ConfigureAwait(false);
            if (!all.IsSuccess)
            {
                return null;
            }
            return all.Value.FirstOrDefault(m =>
                string.Equals(m.Slug, monitorSlug, StringComparison.OrdinalIgnoreCase))?.Url;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex,
                "native-deploy: no se pudo consultar el monitor '{Slug}'; el conflicto se tratará como fallo", monitorSlug);
            return null;
        }
    }

    /// <summary>Resultado de intentar restaurar los reemplazos ya aplicados.</summary>
    /// <param name="Notes">Bitácora por paso, con su nivel, para el log del <c>Deployment</c>.</param>
    /// <param name="Attempted">Cuántos servicios TENÍAN revisión previa que restaurar.</param>
    /// <param name="Complete">Todos los intentados se restauraron Y quedaron sanos.</param>
    private sealed record RestoreOutcome(
        IReadOnlyList<(DeploymentLogLevel Level, string Text)> Notes,
        int Attempted,
        bool Complete);
}
