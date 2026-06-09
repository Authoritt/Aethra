using Aethra.Modules.Deployments.Infrastructure;
using Aethra.Modules.Deployments.Domain.Build;
using Aethra.Modules.Cloudflare.Application.Dtos;
using Aethra.Modules.Cloudflare.UseCases.DnsRecords.Commands;
using Aethra.Modules.Cloudflare.UseCases.DnsRecords.Queries;
using Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands;
using Aethra.Modules.Cloudflare.UseCases.Tunnels.Queries;
using Aethra.Modules.Cloudflare.UseCases.Zones.Queries;
using Aethra.Modules.Monitoring.Infrastructure;
using Aethra.Modules.Monitoring.UseCases.Commands;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Proxy.Infrastructure;
using Aethra.Modules.Proxy.UseCases.Routes.Commands;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Vms.Infrastructure;
using Aethra.Shared.Kernel.Ids;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Aethra.Api.Bootstrap;

/// <summary>
/// Read-only operational layer for the Git -> App Environment -> Machine mental model.
/// It intentionally composes existing module data at the host boundary instead of adding
/// cross-module dependencies between bounded contexts.
/// </summary>
public static class OperationsEndpoints
{
    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ops").WithTags("Operations");

        group.MapGet("/apps", ListApps)
            .RequireAuthorization("scope:projects:read")
            .WithName("ListOperationalApps");

        group.MapGet("/app-environments", ListAppEnvironments)
            .RequireAuthorization("scope:projects:read")
            .WithName("ListOperationalAppEnvironments");

        group.MapGet("/releases", ListReleases)
            .RequireAuthorization("scope:deployments:read")
            .WithName("ListOperationalReleases");

        group.MapGet("/releases/{releaseId}", GetRelease)
            .RequireAuthorization("scope:deployments:read")
            .WithName("GetOperationalRelease");

        group.MapGet("/public-endpoints", ListPublicEndpoints)
            .RequireAuthorization("scope:proxy:read")
            .WithName("ListOperationalPublicEndpoints");

        group.MapGet("/public-access-states", ListPublicAccessStates)
            .RequireAuthorization("scope:proxy:read")
            .WithName("ListOperationalPublicAccessStates");

        group.MapPost("/public-access-states/{appEnvironmentId}/reconcile", ReconcilePublicAccessState)
            .RequireAuthorization("scope:proxy:write")
            .RequireAuthorization("scope:monitoring:write")
            .RequireAuthorization("scope:cloudflare:write")
            .WithName("ReconcileOperationalPublicAccessState");

        group.MapGet("/machines", ListMachines)
            .RequireAuthorization("scope:vms:read")
            .WithName("ListOperationalMachines");

        group.MapGet("/data-services", ListDataServices)
            .RequireAuthorization("scope:services:read")
            .WithName("ListOperationalDataServices");

        group.MapGet("/operational-issues", ListOperationalIssues)
            .RequireAuthorization("scope:projects:read")
            .WithName("ListOperationalIssues");

        return app;
    }

    private static async Task<IResult> ListApps(
        ProjectsDbContext projectsDb,
        DeploymentsDbContext deploymentsDb,
        MonitoringDbContext monitoringDb,
        CancellationToken ct)
    {
        var snapshot = await LoadSnapshot(projectsDb, deploymentsDb, monitoringDb, ct);

        var apps = snapshot.Templates.Values
            .Select(t =>
            {
                var environments = snapshot.Instances.Where(i => i.templateId == t.id).ToList();
                var latestDeployments = environments
                    .Select(e => snapshot.DeploymentsByInstance.GetValueOrDefault(e.id))
                    .Where(d => d is not null)
                    .Select(d => d!)
                    .ToList();
                var failedCount = latestDeployments.Count(d => IsFailed(d.status));
                var activeCount = latestDeployments.Count(d => IsActive(d.status));
                var issueCount = failedCount
                    + environments.Count(e => string.IsNullOrWhiteSpace(e.publicUrl))
                    + environments.Count(e => snapshot.MonitorsByInstance.GetValueOrDefault(e.id)?.status == "Down");

                return new AppOverviewDto(
                    t.id,
                    t.name,
                    t.slug,
                    t.gitRepoUrl,
                    t.defaultBranch,
                    t.projectId,
                    snapshot.Projects.GetValueOrDefault(t.projectId)?.name ?? t.projectId,
                    snapshot.Projects.GetValueOrDefault(t.projectId)?.slug ?? string.Empty,
                    environments.Select(e => e.clientId).Distinct(StringComparer.Ordinal).Count(),
                    environments.Select(e => e.environment).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
                    environments.Count,
                    ResolveAggregateStatus(failedCount, activeCount, issueCount),
                    latestDeployments.OrderByDescending(d => d.createdAt).FirstOrDefault()?.id,
                    latestDeployments.OrderByDescending(d => d.createdAt).FirstOrDefault()?.createdAt,
                    issueCount);
            })
            .OrderBy(a => a.PortfolioName)
            .ThenBy(a => a.Name)
            .ToList();

        return Results.Ok(apps);
    }

    private static async Task<IResult> ListAppEnvironments(
        [FromQuery] string? q,
        [FromQuery] string? status,
        [FromQuery] string? appId,
        [FromQuery] string? environment,
        [FromQuery] string? machineId,
        ProjectsDbContext projectsDb,
        DeploymentsDbContext deploymentsDb,
        MonitoringDbContext monitoringDb,
        VmsDbContext vmsDb,
        CancellationToken ct)
    {
        var snapshot = await LoadSnapshot(projectsDb, deploymentsDb, monitoringDb, ct);
        var vms = await LoadVms(vmsDb, ct);
        var rows = snapshot.Instances
            .Select(i => ToAppEnvironment(i, snapshot, vms))
            .Where(r => MatchesAppEnvironmentFilters(r, q, status, appId, environment, machineId))
            .OrderBy(r => r.AppName)
            .ThenBy(r => r.TenantName)
            .ThenBy(r => r.Environment)
            .ToList();

        return Results.Ok(rows);
    }

    private static async Task<IResult> ListReleases(
        [FromQuery] string? q,
        [FromQuery] string? status,
        [FromQuery] string? appId,
        [FromQuery] string? gitRef,
        ProjectsDbContext projectsDb,
        DeploymentsDbContext deploymentsDb,
        CancellationToken ct)
    {
        var releases = await LoadReleases(projectsDb, deploymentsDb, releaseId: null, take: 100, ct);
        return Results.Ok(releases.Where(r => MatchesReleaseFilters(r, q, status, appId, gitRef)).ToList());
    }

    private static async Task<IResult> GetRelease(
        string releaseId,
        ProjectsDbContext projectsDb,
        DeploymentsDbContext deploymentsDb,
        CancellationToken ct)
    {
        var releases = await LoadReleases(projectsDb, deploymentsDb, releaseId, take: null, ct);
        return releases.Count == 0 ? Results.NotFound() : Results.Ok(releases[0]);
    }

    private static async Task<List<ReleaseOverviewDto>> LoadReleases(
        ProjectsDbContext projectsDb,
        DeploymentsDbContext deploymentsDb,
        string? releaseId,
        int? take,
        CancellationToken ct)
    {
        var projects = await LoadProjects(projectsDb, ct);
        var templates = await LoadTemplates(projectsDb, ct);
        var instances = await LoadInstances(projectsDb, ct);
        var clients = await LoadClients(projectsDb, ct);

        var buildsQuery = deploymentsDb.Builds.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(releaseId))
        {
            if (!AethraId.TryParse(releaseId, out var parsed) || parsed.Value.Prefix != "bld")
            {
                return [];
            }

            var typed = new BuildId(parsed.Value);
            buildsQuery = buildsQuery.Where(b => b.Id == typed);
        }

        buildsQuery = buildsQuery.OrderByDescending(b => b.CreatedAt);
        if (take is not null)
        {
            buildsQuery = buildsQuery.Take(take.Value);
        }

        var builds = await buildsQuery
            .Select(b => new
            {
                id = b.Id.ToString(),
                templateId = b.TemplateId,
                gitSha = b.GitSha,
                gitRef = b.GitRef,
                status = b.Status.ToString(),
                trigger = b.Trigger.ToString(),
                triggeredBy = b.TriggeredBy,
                imageRef = b.ImageRef,
                createdAt = b.CreatedAt,
                startedAt = b.StartedAt,
                finishedAt = b.FinishedAt,
                errorCode = b.ErrorCode,
                errorMessage = b.ErrorMessage
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var buildIds = builds.Select(b => b.id).ToList();
        var deployments = await deploymentsDb.Deployments.AsNoTracking()
            .Where(d => buildIds.Contains(d.BuildId))
            .Select(d => new
            {
                id = d.Id.ToString(),
                buildId = d.BuildId,
                instanceId = d.InstanceId,
                status = d.Status.ToString(),
                createdAt = d.CreatedAt,
                finishedAt = d.FinishedAt,
                errorCode = d.ErrorCode,
                errorMessage = d.ErrorMessage
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var deploymentsByBuild = deployments
            .GroupBy(d => d.buildId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var releases = builds.Select(b =>
        {
            var template = templates.GetValueOrDefault(b.templateId);
            var project = template is null ? null : projects.GetValueOrDefault(template.projectId);
            var fanout = deploymentsByBuild.GetValueOrDefault(b.id) ?? [];
            var affected = fanout
                .Select(d =>
                {
                    var instance = instances.GetValueOrDefault(d.instanceId);
                    if (instance is null)
                    {
                        return null;
                    }
                    var client = clients.GetValueOrDefault(instance.clientId);
                    return new ReleaseTargetDto(
                        d.id,
                        instance.id,
                        instance.slug,
                        client?.id,
                        client?.displayName ?? instance.clientSlug,
                        instance.environment,
                        d.status,
                        d.errorCode,
                        d.errorMessage);
                })
                .Where(x => x is not null)
                .Select(x => x!)
                .ToList();

            var failed = fanout.Count(d => IsFailed(d.status));
            var active = fanout.Count(d => IsActive(d.status));
            var completed = fanout.Count(d => string.Equals(d.status, "Completed", StringComparison.OrdinalIgnoreCase));
            var status = IsFailed(b.status) || failed > 0
                ? "failed"
                : IsActive(b.status) || active > 0
                    ? "active"
                    : string.Equals(b.status, "Completed", StringComparison.OrdinalIgnoreCase)
                        ? "healthy"
                        : "unknown";

            return new ReleaseOverviewDto(
                b.id,
                b.id,
                template?.id,
                template?.name ?? b.templateId,
                template?.slug ?? string.Empty,
                project?.id,
                project?.name ?? string.Empty,
                b.gitSha,
                ShortSha(b.gitSha),
                b.gitRef,
                b.trigger,
                b.triggeredBy,
                status,
                b.status,
                fanout.Count,
                completed,
                failed,
                active,
                b.createdAt,
                b.startedAt,
                b.finishedAt,
                b.imageRef,
                b.errorCode,
                b.errorMessage,
                affected);
        }).ToList();

        return releases;
    }

    private static async Task<IResult> ListPublicEndpoints(
        [FromQuery] string? q,
        [FromQuery] string? health,
        [FromQuery] string? appId,
        [FromQuery] string? environment,
        [FromQuery] string? dns,
        [FromQuery] string? tunnel,
        [FromQuery] string? monitor,
        ProjectsDbContext projectsDb,
        ProxyDbContext proxyDb,
        MonitoringDbContext monitoringDb,
        IMediator mediator,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var snapshot = await LoadSnapshot(projectsDb, null, monitoringDb, ct);
        var publicInfra = await LoadPublicAccessInfra(mediator, configuration, ct);
        // Hostname es un value object (value-converted): ordenar/proyectar .Value no se traduce a SQL.
        // Materializamos las entidades y proyectamos + ordenamos en memoria (el set de rutas es chico).
        var routeEntities = await proxyDb.Routes.AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var routes = routeEntities
            .Select(r => new RouteRow(r.Id.ToString(), r.Hostname.Value, r.PathPrefix, r.BackendUrl, r.TlsEnabled))
            .OrderBy(r => r.hostname, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.pathPrefix, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var groups = routes
            .GroupBy(r => r.hostname, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                return BuildPublicEndpoint(g.Key, g.ToList(), snapshot, publicInfra);
            })
            .Where(e => MatchesPublicEndpointFilters(e, q, health, appId, environment, dns, tunnel, monitor))
            .OrderBy(e => e.HealthStatus == "healthy")
            .ThenBy(e => e.Hostname)
            .ToList();

        return Results.Ok(groups);
    }

    private static async Task<IResult> ListPublicAccessStates(
        [FromQuery] string? appEnvironmentId,
        ProjectsDbContext projectsDb,
        ProxyDbContext proxyDb,
        MonitoringDbContext monitoringDb,
        IMediator mediator,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var snapshot = await LoadSnapshot(projectsDb, null, monitoringDb, ct);
        var routesByHost = await LoadRoutesByHost(proxyDb, ct);
        var publicInfra = await LoadPublicAccessInfra(mediator, configuration, ct);

        var instances = snapshot.Instances.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(appEnvironmentId))
        {
            instances = instances.Where(i => string.Equals(i.id, appEnvironmentId, StringComparison.Ordinal));
        }

        var states = instances.Select(instance => BuildPublicAccessState(instance, snapshot, routesByHost, publicInfra))
        .OrderBy(s => s.HealthStatus == "healthy")
        .ThenBy(s => s.AppName)
        .ThenBy(s => s.TenantName)
        .ThenBy(s => s.Environment)
        .ToList();

        return Results.Ok(states);
    }

    private static async Task<IResult> ReconcilePublicAccessState(
        string appEnvironmentId,
        [FromBody] PublicAccessReconcileRequest? request,
        ProjectsDbContext projectsDb,
        ProxyDbContext proxyDb,
        MonitoringDbContext monitoringDb,
        IMediator mediator,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var dryRun = request?.DryRun ?? false;
        var snapshot = await LoadSnapshot(projectsDb, null, monitoringDb, ct);
        var instance = snapshot.Instances.FirstOrDefault(i => string.Equals(i.id, appEnvironmentId, StringComparison.Ordinal));
        if (instance is null)
        {
            return Results.NotFound(new { Code = "app_environment.not_found", Message = $"App Environment '{appEnvironmentId}' no existe." });
        }

        var routesByHost = await LoadRoutesByHost(proxyDb, ct);
        var publicInfra = await LoadPublicAccessInfra(mediator, configuration, ct);
        var state = BuildPublicAccessState(instance, snapshot, routesByHost, publicInfra);
        var actions = new List<PublicAccessReconcileActionDto>();
        var desiredHostname = state.DesiredHostname;

        if (string.IsNullOrWhiteSpace(desiredHostname))
        {
            actions.Add(new PublicAccessReconcileActionDto(
                "set_hostname",
                "blocked",
                "No hay custom domain ni auto hostname configurado para este App Environment.",
                null,
                null,
                null));
            return Results.Ok(new PublicAccessReconcileResultDto(appEnvironmentId, dryRun, false, actions, state));
        }

        var dnsRecord = FindDnsRecord(desiredHostname, publicInfra);
        var zone = FindZoneForHostname(desiredHostname, publicInfra);
        if (zone is null)
        {
            actions.Add(new PublicAccessReconcileActionDto(
                "register_dns_zone",
                "blocked",
                $"No hay zona Cloudflare registrada que cubra {desiredHostname}.",
                null,
                "cloudflare.zone_missing",
                "Registra la zona en Settings/Domains o Cloudflare antes de crear DNS."));
        }
        else if (string.IsNullOrWhiteSpace(publicInfra.TunnelCname))
        {
            actions.Add(new PublicAccessReconcileActionDto(
                "configure_dns_target",
                "blocked",
                "NativeDeploy:TunnelCname no esta configurado; no se conoce el target CNAME esperado.",
                null,
                "cloudflare.tunnel_cname_missing",
                "Configura NativeDeploy:TunnelCname para que Aethra pueda crear o reparar DNS."));
        }
        else if (dnsRecord is null)
        {
            await ApplyAction(
                actions,
                dryRun,
                "create_dns_record",
                $"Crear CNAME proxied {desiredHostname} -> {publicInfra.TunnelCname}.",
                async () =>
                {
                    var result = await mediator.Send(new CreateDnsRecordCommand(
                        ZoneId: zone.Id,
                        Type: "CNAME",
                        Name: desiredHostname,
                        Content: publicInfra.TunnelCname,
                        Ttl: 1,
                        Proxied: true,
                        Comment: "aethra operational public-access reconcile"), ct).ConfigureAwait(false);
                    return result.IsSuccess
                        ? new AppliedResource(result.Value.Id, null, null)
                        : new AppliedResource(null, result.Error.Code, result.Error.Message);
                });
        }
        else if (!string.Equals(dnsRecord.content, publicInfra.TunnelCname, StringComparison.OrdinalIgnoreCase) || !dnsRecord.proxied)
        {
            await ApplyAction(
                actions,
                dryRun,
                "update_dns_record",
                $"Actualizar DNS {dnsRecord.id} para apuntar a {publicInfra.TunnelCname} con proxy activo.",
                async () =>
                {
                    var result = await mediator.Send(new UpdateDnsRecordCommand(
                        RecordId: dnsRecord.id,
                        Content: publicInfra.TunnelCname,
                        Ttl: 1,
                        Proxied: true,
                        Comment: "aethra operational public-access reconcile"), ct).ConfigureAwait(false);
                    return result.IsSuccess
                        ? new AppliedResource(result.Value.Id, null, null)
                        : new AppliedResource(null, result.Error.Code, result.Error.Message);
                });
        }
        else
        {
            actions.Add(new PublicAccessReconcileActionDto(
                "dns",
                "skipped",
                "El DNS ya existe y apunta al CNAME esperado.",
                dnsRecord.id,
                null,
                null));
        }

        if (publicInfra.Tunnel is null)
        {
            actions.Add(new PublicAccessReconcileActionDto(
                "register_tunnel",
                "blocked",
                "No hay Cloudflare Tunnel gestionado registrado.",
                null,
                "cloudflare.tunnel_missing",
                "Registra o promueve un tunnel remoto antes de asegurar ingress."));
        }
        else if (!HasTunnelIngress(desiredHostname, publicInfra))
        {
            await ApplyAction(
                actions,
                dryRun,
                "ensure_tunnel_ingress",
                $"Asegurar ingress del tunnel para {desiredHostname}.",
                async () =>
                {
                    var result = await mediator.Send(new EnsureTunnelHostnameCommand(desiredHostname), ct).ConfigureAwait(false);
                    return result.IsSuccess
                        ? new AppliedResource(publicInfra.Tunnel.Id, null, null)
                        : new AppliedResource(null, result.Error.Code, result.Error.Message);
                });
        }
        else
        {
            actions.Add(new PublicAccessReconcileActionDto(
                "tunnel",
                "skipped",
                "El tunnel ya tiene ingress para el hostname.",
                publicInfra.Tunnel.Id,
                null,
                null));
        }

        var backendUrl = BuildBackendUrl(instance);
        var existingRoutes = routesByHost.GetValueOrDefault(desiredHostname) ?? [];
        var mainRoute = existingRoutes.FirstOrDefault(r => r.pathPrefix == "/") ?? existingRoutes.FirstOrDefault();
        if (backendUrl is null)
        {
            actions.Add(new PublicAccessReconcileActionDto(
                "configure_primary_port",
                "blocked",
                "El App Environment no tiene puerto primario; no se puede construir el backend URL de la Route.",
                null,
                "app_environment.primary_port_missing",
                "Configura al menos un puerto del contenedor para que Aethra pueda crear la Route."));
        }
        else if (mainRoute is null)
        {
            await ApplyAction(
                actions,
                dryRun,
                "create_route",
                $"Crear Route {desiredHostname} -> {backendUrl}.",
                async () =>
                {
                    var result = await mediator.Send(new CreateRouteCommand(desiredHostname, backendUrl, TlsEnabled: true, PathPrefix: "/"), ct).ConfigureAwait(false);
                    return result.IsSuccess
                        ? new AppliedResource(result.Value.Id, null, null)
                        : new AppliedResource(null, result.Error.Code, result.Error.Message);
                });
        }
        else if (!string.Equals(mainRoute.backendUrl, backendUrl, StringComparison.OrdinalIgnoreCase) || !mainRoute.tlsEnabled)
        {
            await ApplyAction(
                actions,
                dryRun,
                "update_route",
                $"Actualizar Route {mainRoute.id} -> {backendUrl} con TLS activo.",
                async () =>
                {
                    var result = await mediator.Send(new UpdateRouteCommand(mainRoute.id, backendUrl, TlsEnabled: true), ct).ConfigureAwait(false);
                    return result.IsSuccess
                        ? new AppliedResource(mainRoute.id, null, null)
                        : new AppliedResource(null, result.Error.Code, result.Error.Message);
                });
        }
        else
        {
            actions.Add(new PublicAccessReconcileActionDto(
                "route",
                "skipped",
                "La Route principal ya apunta al backend esperado y tiene TLS activo.",
                mainRoute.id,
                null,
                null));
        }

        var monitor = snapshot.MonitorsByUrlHost.GetValueOrDefault(desiredHostname);
        if (monitor is null)
        {
            var template = snapshot.Templates.GetValueOrDefault(instance.templateId);
            await ApplyAction(
                actions,
                dryRun,
                "create_monitor",
                $"Crear Monitor HTTP para https://{desiredHostname}/.",
                async () =>
                {
                    var result = await mediator.Send(new CreateMonitorCommand(
                        Slug: SafeSlug(desiredHostname),
                        Name: $"Health: {desiredHostname}",
                        Url: $"https://{desiredHostname}/",
                        HttpMethod: "GET",
                        ExpectedStatusCodes: [200, 204, 301, 302],
                        IntervalSec: 60,
                        TimeoutMs: 10000,
                        Headers: null,
                        BodyTemplate: null,
                        InstanceId: instance.id,
                        ProjectId: template?.projectId), ct).ConfigureAwait(false);
                    return result.IsSuccess
                        ? new AppliedResource(result.Value.Id, null, null)
                        : new AppliedResource(null, result.Error.Code, result.Error.Message);
                });
        }
        else if (monitor.status == "Down")
        {
            await ApplyAction(
                actions,
                dryRun,
                "trigger_monitor",
                $"Ejecutar check manual del Monitor {monitor.id}.",
                async () =>
                {
                    var result = await mediator.Send(new TriggerMonitorCheckCommand(monitor.id), ct).ConfigureAwait(false);
                    return result.IsSuccess
                        ? new AppliedResource(monitor.id, null, null)
                        : new AppliedResource(null, result.Error.Code, result.Error.Message);
                });
        }
        else
        {
            actions.Add(new PublicAccessReconcileActionDto(
                "monitor",
                "skipped",
                "El Monitor ya existe y no esta reportando Down.",
                monitor.id,
                null,
                null));
        }

        var applied = !dryRun && actions.Any(a => a.Status == "applied");
        var refreshedState = state;
        if (applied)
        {
            var refreshedSnapshot = await LoadSnapshot(projectsDb, null, monitoringDb, ct);
            var refreshedRoutesByHost = await LoadRoutesByHost(proxyDb, ct);
            var refreshedPublicInfra = await LoadPublicAccessInfra(mediator, configuration, ct);
            var refreshedInstance = refreshedSnapshot.Instances.First(i => string.Equals(i.id, appEnvironmentId, StringComparison.Ordinal));
            refreshedState = BuildPublicAccessState(refreshedInstance, refreshedSnapshot, refreshedRoutesByHost, refreshedPublicInfra);
        }

        return Results.Ok(new PublicAccessReconcileResultDto(appEnvironmentId, dryRun, applied, actions, refreshedState));
    }

    private static async Task<IResult> ListMachines(
        [FromQuery] string? q,
        [FromQuery] string? readiness,
        [FromQuery] bool? acceptsPreviews,
        ProjectsDbContext projectsDb,
        DeploymentsDbContext deploymentsDb,
        MonitoringDbContext monitoringDb,
        VmsDbContext vmsDb,
        CancellationToken ct)
    {
        var snapshot = await LoadSnapshot(projectsDb, deploymentsDb, monitoringDb, ct);
        var vms = await LoadVms(vmsDb, ct);

        var workloadsByVm = snapshot.Instances
            .GroupBy(i => i.targetVmId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var machines = vms.Values
            .Select(vm =>
            {
                var workloads = workloadsByVm.GetValueOrDefault(vm.id) ?? [];
                var workloadDtos = workloads
                    .Select(i =>
                    {
                        var env = ToAppEnvironment(i, snapshot, vms);
                        return new MachineWorkloadDto(
                            env.Id,
                            env.Slug,
                            env.AppId,
                            env.AppName,
                            env.TenantId,
                            env.TenantName,
                            env.Environment,
                            env.HealthStatus,
                            env.LatestReleaseStatus,
                            env.PublicUrl,
                            env.IssueCount,
                            env.IsEphemeral);
                    })
                    .OrderBy(w => w.AppName)
                    .ThenBy(w => w.TenantName)
                    .ThenBy(w => w.Environment)
                    .ToList();
                var failing = workloadDtos.Count(w => w.HealthStatus is "failed" or "degraded");
                var deploying = workloadDtos.Count(w => w.HealthStatus == "deploying");
                var preview = workloadDtos.Count(w => w.IsEphemeral);
                var readiness = ResolveMachineReadiness(vm.status, failing, deploying);

                return new MachineOverviewDto(
                    vm.id,
                    vm.name,
                    vm.slug,
                    vm.status,
                    readiness,
                    workloadDtos.Count,
                    failing,
                    deploying,
                    preview,
                    vm.acceptsPreviews,
                    vm.lastConnectedAt,
                    vm.lastSeenAt,
                    vm.updatedAt,
                    workloadDtos);
            })
            .Where(m => MatchesMachineFilters(m, q, readiness, acceptsPreviews))
            .OrderBy(m => ReadinessRank(m.ReadinessStatus))
            .ThenBy(m => m.Name)
            .ToList();

        return Results.Ok(machines);
    }

    private static async Task<IResult> ListDataServices(
        [FromQuery] string? q,
        [FromQuery] string? status,
        [FromQuery] string? type,
        [FromQuery] string? appEnvironmentId,
        ProjectsDbContext projectsDb,
        ServicesDbContext servicesDb,
        CancellationToken ct)
    {
        var projects = await LoadProjects(projectsDb, ct);
        var templates = await LoadTemplates(projectsDb, ct);
        var clients = await LoadClients(projectsDb, ct);
        var instances = await LoadInstances(projectsDb, ct);

        var serviceEntities = await servicesDb.ManagedServices.AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var services = serviceEntities.Select(s => new
            {
                id = s.Id.ToString(),
                s.Slug,
                s.Name,
                type = s.Type.ToString(),
                s.Version,
                status = s.Status.ToString(),
                s.TargetVmId,
                s.ContainerName,
                s.ExposedExternally,
                s.CreatedAt,
                s.UpdatedAt,
                s.ProvisionedAt,
                s.LastBackupAt,
                s.LastRestoredAt,
                s.ErrorCode,
                s.ErrorMessage
            })
            .ToList();

        var bindingEntities = await servicesDb.ServiceBindings.AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var bindings = bindingEntities.Select(b => new
            {
                id = b.Id.ToString(),
                serviceId = b.ServiceId.ToString(),
                b.InstanceId,
                b.ResourceName,
                permissions = b.Permissions.ToString(),
                envVarPrefix = b.InjectedEnvVarPrefix,
                hasMigrationsHook = b.MigrationsHook != null,
                b.CreatedAt,
                b.ProvisionedAt,
                b.RevokedAt,
                b.LastRotatedAt
            })
            .ToList();

        var bindingsByService = bindings
            .GroupBy(b => b.serviceId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var rows = services.Select(service =>
        {
            var serviceBindings = bindingsByService.GetValueOrDefault(service.id) ?? [];
            var bindingDtos = serviceBindings.Select(binding =>
            {
                instances.TryGetValue(binding.InstanceId, out var instance);
                TemplateRow? template = null;
                ProjectRow? project = null;
                ClientRow? client = null;
                if (instance is not null)
                {
                    template = templates.GetValueOrDefault(instance.templateId);
                    project = template is null ? null : projects.GetValueOrDefault(template.projectId);
                    client = clients.GetValueOrDefault(instance.clientId);
                }

                return new DataServiceBindingOverviewDto(
                    binding.id,
                    binding.InstanceId,
                    instance?.slug,
                    template?.id,
                    template?.name,
                    project?.id,
                    project?.name,
                    client?.id,
                    client?.displayName ?? instance?.clientSlug,
                    instance?.environment,
                    binding.ResourceName,
                    binding.permissions,
                    binding.envVarPrefix,
                    binding.hasMigrationsHook,
                    binding.ProvisionedAt is not null && binding.RevokedAt is null ? "ready" : binding.RevokedAt is not null ? "revoked" : "provisioning",
                    binding.CreatedAt,
                    binding.ProvisionedAt,
                    binding.RevokedAt,
                    binding.LastRotatedAt);
            }).ToList();

            return new DataServiceOverviewDto(
                service.id,
                service.Slug,
                service.Name,
                service.type,
                service.Version,
                service.status,
                service.TargetVmId,
                service.ContainerName,
                service.ExposedExternally,
                service.CreatedAt,
                service.UpdatedAt,
                service.ProvisionedAt,
                service.LastBackupAt,
                service.LastRestoredAt,
                service.ErrorCode,
                service.ErrorMessage,
                bindingDtos.Count(b => b.RevokedAt is null),
                bindingDtos);
        })
        .Where(row => MatchesDataServiceFilters(row, q, status, type, appEnvironmentId))
        .OrderBy(row => row.Status == "Ready")
        .ThenBy(row => row.Name)
        .ToList();

        return Results.Ok(rows);
    }

    private static async Task<IResult> ListOperationalIssues(
        [FromQuery] string? q,
        [FromQuery] string? severity,
        [FromQuery] string? resourceType,
        [FromQuery] string? appId,
        ProjectsDbContext projectsDb,
        DeploymentsDbContext deploymentsDb,
        ProxyDbContext proxyDb,
        MonitoringDbContext monitoringDb,
        VmsDbContext vmsDb,
        IMediator mediator,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var snapshot = await LoadSnapshot(projectsDb, deploymentsDb, monitoringDb, ct);
        var vms = await LoadVms(vmsDb, ct);
        var issues = new List<OperationalIssueDto>();

        foreach (var env in snapshot.Instances)
        {
            var app = snapshot.Templates.GetValueOrDefault(env.templateId);
            var client = snapshot.Clients.GetValueOrDefault(env.clientId);
            var latestDeployment = snapshot.DeploymentsByInstance.GetValueOrDefault(env.id);
            var monitor = snapshot.MonitorsByInstance.GetValueOrDefault(env.id);
            var vm = vms.GetValueOrDefault(env.targetVmId);

            if (string.IsNullOrWhiteSpace(env.publicUrl))
            {
                issues.Add(Issue("app_environment.no_public_url", "warning", "App Environment has no public URL", env.id, app?.id, app?.name, client?.displayName, env.environment, env.updatedAt));
            }
            if (latestDeployment is { } d && IsFailed(d.status))
            {
                issues.Add(Issue("release.deploy_failed", "critical", d.errorMessage ?? "Deployment failed", env.id, app?.id, app?.name, client?.displayName, env.environment, d.finishedAt ?? d.createdAt));
            }
            if (monitor?.status == "Down")
            {
                issues.Add(Issue("monitor.down", "critical", $"Monitor down: {monitor.name}", env.id, app?.id, app?.name, client?.displayName, env.environment, monitor.lastCheckedAt ?? env.updatedAt));
            }
            if (vm?.status == "Disconnected")
            {
                issues.Add(Issue("machine.disconnected", "critical", $"Machine disconnected: {vm.name}", env.id, app?.id, app?.name, client?.displayName, env.environment, vm.updatedAt));
            }
        }

        var failedBuilds = await deploymentsDb.Builds.AsNoTracking()
            .Where(b => b.Status == BuildStatus.Failed)
            .OrderByDescending(b => b.FinishedAt ?? b.CreatedAt)
            .Take(25)
            .Select(b => new { id = b.Id.ToString(), b.TemplateId, b.ErrorMessage, at = b.FinishedAt ?? b.CreatedAt })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var build in failedBuilds)
        {
            var app = snapshot.Templates.GetValueOrDefault(build.TemplateId);
            issues.Add(new OperationalIssueDto(
                $"build:{build.id}",
                "release.build_failed",
                "critical",
                build.ErrorMessage ?? "Build failed",
                "Release",
                build.id,
                null,
                app?.id,
                app?.name,
                null,
                null,
                build.at,
                "Open build logs",
                $"/builds/{build.id}"));
        }

        var routes = await proxyDb.Routes.AsNoTracking()
            .Select(r => new RouteRow(r.Id.ToString(), r.Hostname.Value, r.PathPrefix, r.BackendUrl, r.TlsEnabled))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var publicInfra = await LoadPublicAccessInfra(mediator, configuration, ct);
        foreach (var group in routes.GroupBy(r => r.hostname, StringComparer.OrdinalIgnoreCase))
        {
            var endpoint = BuildPublicEndpoint(group.Key, group.ToList(), snapshot, publicInfra);
            foreach (var code in endpoint.Issues)
            {
                issues.Add(new OperationalIssueDto(
                    $"endpoint:{endpoint.Hostname}:{code}",
                    code,
                    code is "route.owner_missing" or "endpoint.dns_zone_missing" or "endpoint.dns_missing" or "endpoint.dns_target_mismatch" or "endpoint.tunnel_missing" or "endpoint.tunnel_ingress_missing" or "monitor.down" ? "critical" : "warning",
                    $"{endpoint.Hostname}: {code}",
                    "PublicEndpoint",
                    endpoint.Hostname,
                    endpoint.AppEnvironmentId,
                    endpoint.AppId,
                    endpoint.AppName,
                    endpoint.TenantName,
                    endpoint.Environment,
                    null,
                    SuggestedEndpointAction(code, endpoint.AppEnvironmentId),
                    endpoint.AppEnvironmentId is null
                        ? $"/public-access?q={Uri.EscapeDataString(endpoint.Hostname)}"
                        : $"/instances/{endpoint.AppEnvironmentId}"));
            }
        }

        return Results.Ok(issues
            .Where(i => MatchesOperationalIssueFilters(i, q, severity, resourceType, appId))
            .OrderByDescending(i => SeverityRank(i.Severity))
            .ThenBy(i => i.Code)
            .ToList());
    }

    private static async Task ApplyAction(
        List<PublicAccessReconcileActionDto> actions,
        bool dryRun,
        string kind,
        string message,
        Func<Task<AppliedResource>> apply)
    {
        if (dryRun)
        {
            actions.Add(new PublicAccessReconcileActionDto(kind, "planned", message, null, null, null));
            return;
        }

        var result = await apply().ConfigureAwait(false);
        actions.Add(result.errorCode is null
            ? new PublicAccessReconcileActionDto(kind, "applied", message, result.resourceId, null, null)
            : new PublicAccessReconcileActionDto(kind, "failed", message, null, result.errorCode, result.errorMessage));
    }

    private static async Task<Dictionary<string, List<RouteRow>>> LoadRoutesByHost(ProxyDbContext proxyDb, CancellationToken ct)
    {
        var routeEntities = await proxyDb.Routes.AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return routeEntities
            .Select(r => new RouteRow(r.Id.ToString(), r.Hostname.Value, r.PathPrefix, r.BackendUrl, r.TlsEnabled))
            .GroupBy(r => r.hostname, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<PublicAccessInfraSnapshot> LoadPublicAccessInfra(
        IMediator mediator,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var tunnelCname = configuration["NativeDeploy:TunnelCname"];
        var zonesResult = await mediator.Send(new ListZonesQuery(), ct).ConfigureAwait(false);
        IReadOnlyList<CloudflareZoneDto> zones = zonesResult.IsSuccess ? zonesResult.Value : [];
        var dnsRecords = new List<DnsRecordRow>();

        foreach (var zone in zones)
        {
            var recordsResult = await mediator.Send(new ListDnsRecordsQuery(zone.Id), ct).ConfigureAwait(false);
            if (recordsResult.IsFailure)
            {
                continue;
            }

            dnsRecords.AddRange(recordsResult.Value.Select(r => new DnsRecordRow(
                r.Id,
                r.ZoneId,
                zone.Name,
                r.Type,
                r.Name,
                r.Content,
                r.Proxied,
                r.SyncedAt is not null,
                r.LastError)));
        }

        CloudflareTunnelDto? tunnel = null;
        var tunnelResult = await mediator.Send(new GetTunnelQuery(), ct).ConfigureAwait(false);
        if (tunnelResult.IsSuccess)
        {
            tunnel = tunnelResult.Value;
        }

        return new PublicAccessInfraSnapshot(tunnelCname, zones, dnsRecords, tunnel);
    }

    private static DnsRecordRow? FindDnsRecord(string hostname, PublicAccessInfraSnapshot infra)
        => infra.DnsRecords
            .Where(r => string.Equals(r.name, hostname, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.synced)
            .ThenByDescending(r => r.zoneName.Length)
            .FirstOrDefault();

    private static CloudflareZoneDto? FindZoneForHostname(string hostname, PublicAccessInfraSnapshot infra)
        => infra.Zones
            .Where(z => hostname.Equals(z.Name, StringComparison.OrdinalIgnoreCase)
                || hostname.EndsWith("." + z.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(z => z.Name.Length)
            .FirstOrDefault();

    private static bool HasTunnelIngress(string hostname, PublicAccessInfraSnapshot infra)
        => infra.Tunnel?.Ingress.Any(r => string.Equals(r.Hostname, hostname, StringComparison.OrdinalIgnoreCase)) == true;

    private static PublicAccessStateDto BuildPublicAccessState(
        InstanceRow instance,
        OpsSnapshot snapshot,
        IReadOnlyDictionary<string, List<RouteRow>> routesByHost,
        PublicAccessInfraSnapshot publicInfra)
    {
        var template = snapshot.Templates.GetValueOrDefault(instance.templateId);
        var client = snapshot.Clients.GetValueOrDefault(instance.clientId);
        var desiredHostname = instance.customDomain ?? instance.autoHostname;
        var desiredSource = instance.customDomain is not null ? "custom_domain" : instance.autoHostname is not null ? "auto_hostname" : "none";
        var routes = desiredHostname is null ? [] : routesByHost.GetValueOrDefault(desiredHostname) ?? [];
        var monitor = desiredHostname is null ? null : snapshot.MonitorsByUrlHost.GetValueOrDefault(desiredHostname);
        var dnsRecord = desiredHostname is null ? null : FindDnsRecord(desiredHostname, publicInfra);
        var zone = desiredHostname is null ? null : FindZoneForHostname(desiredHostname, publicInfra);
        var dnsTargetConfigured = string.IsNullOrWhiteSpace(publicInfra.TunnelCname)
            || string.Equals(dnsRecord?.content, publicInfra.TunnelCname, StringComparison.OrdinalIgnoreCase);
        var tunnelConfigured = desiredHostname is not null && HasTunnelIngress(desiredHostname, publicInfra);
        var issues = new List<string>();

        if (desiredHostname is null)
        {
            issues.Add("desired_hostname_missing");
        }
        if (desiredHostname is not null && zone is null)
        {
            issues.Add("dns_zone_missing");
        }
        if (desiredHostname is not null && zone is not null && dnsRecord is null)
        {
            issues.Add("dns_record_missing");
        }
        if (dnsRecord is not null && !dnsTargetConfigured)
        {
            issues.Add("dns_target_mismatch");
        }
        if (desiredHostname is not null && publicInfra.Tunnel is null)
        {
            issues.Add("tunnel_missing");
        }
        if (desiredHostname is not null && publicInfra.Tunnel is not null && !tunnelConfigured)
        {
            issues.Add("tunnel_ingress_missing");
        }
        if (desiredHostname is not null && routes.Count == 0)
        {
            issues.Add("route_missing");
        }
        if (routes.Count > 0 && !routes.Any(r => r.tlsEnabled))
        {
            issues.Add("tls_missing");
        }
        if (desiredHostname is not null && monitor is null)
        {
            issues.Add("monitor_missing");
        }
        if (monitor?.status == "Down")
        {
            issues.Add("monitor_down");
        }

        var health = issues.Count == 0
            ? "healthy"
            : issues.Any(i => i is "desired_hostname_missing" or "dns_zone_missing" or "dns_record_missing" or "dns_target_mismatch" or "tunnel_missing" or "tunnel_ingress_missing" or "route_missing" or "monitor_down")
                ? "broken"
                : "degraded";
        var nextAction = issues.Contains("desired_hostname_missing", StringComparer.Ordinal)
            ? "set_hostname"
            : issues.Contains("dns_zone_missing", StringComparer.Ordinal)
                ? "register_dns_zone"
                : issues.Contains("dns_record_missing", StringComparer.Ordinal) || issues.Contains("dns_target_mismatch", StringComparer.Ordinal)
                    ? "ensure_dns"
                    : issues.Contains("tunnel_missing", StringComparer.Ordinal)
                        ? "register_tunnel"
                        : issues.Contains("tunnel_ingress_missing", StringComparer.Ordinal)
                            ? "ensure_tunnel"
                            : issues.Contains("route_missing", StringComparer.Ordinal)
                                ? "create_route"
                                : issues.Contains("tls_missing", StringComparer.Ordinal)
                                    ? "enable_tls"
                                    : issues.Contains("monitor_missing", StringComparer.Ordinal)
                                        ? "create_monitor"
                                        : issues.Contains("monitor_down", StringComparer.Ordinal)
                                            ? "fix_monitor"
                                            : "none";

        return new PublicAccessStateDto(
            instance.id,
            instance.slug,
            template?.id,
            template?.name,
            client?.id,
            client?.displayName ?? instance.clientSlug,
            instance.environment,
            desiredHostname,
            desiredHostname is null ? null : $"https://{desiredHostname}",
            desiredSource,
            health,
            nextAction,
            dnsRecord is not null,
            dnsRecord?.content,
            publicInfra.TunnelCname,
            tunnelConfigured,
            publicInfra.Tunnel?.Name,
            routes.Count > 0,
            routes.Any(r => r.tlsEnabled),
            monitor is not null,
            monitor?.status,
            routes.Select(r => new PublicEndpointRouteDto(r.id, r.pathPrefix, r.backendUrl)).ToList(),
            issues);
    }

    private static async Task<OpsSnapshot> LoadSnapshot(
        ProjectsDbContext projectsDb,
        DeploymentsDbContext? deploymentsDb,
        MonitoringDbContext monitoringDb,
        CancellationToken ct)
    {
        var projects = await LoadProjects(projectsDb, ct);
        var templates = await LoadTemplates(projectsDb, ct);
        var clients = await LoadClients(projectsDb, ct);
        var instances = await LoadInstances(projectsDb, ct);

        var monitors = await monitoringDb.Monitors.AsNoTracking()
            .Select(m => new MonitorRow(
                m.Id.ToString(),
                m.Name,
                m.Url,
                m.InstanceId,
                m.ProjectId,
                m.IsEnabled,
                m.LastStatus.ToString(),
                m.LastCheckedAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var deploymentsByInstance = new Dictionary<string, DeploymentRow>(StringComparer.Ordinal);
        if (deploymentsDb is not null)
        {
            var deployments = await deploymentsDb.Deployments.AsNoTracking()
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new DeploymentRow(
                    d.Id.ToString(),
                    d.BuildId,
                    d.InstanceId,
                    d.Status.ToString(),
                    d.CreatedAt,
                    d.StartedAt,
                    d.FinishedAt,
                    d.NewImageRef,
                    d.ErrorCode,
                    d.ErrorMessage))
                .ToListAsync(ct)
                .ConfigureAwait(false);
            deploymentsByInstance = deployments
                .GroupBy(d => d.instanceId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.createdAt).First(), StringComparer.Ordinal);
        }

        return new OpsSnapshot(
            projects,
            templates,
            clients,
            instances.Values.ToList(),
            deploymentsByInstance,
            monitors
                .Where(m => !string.IsNullOrWhiteSpace(m.instanceId))
                .GroupBy(m => m.instanceId!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.lastCheckedAt).First(), StringComparer.Ordinal),
            monitors
                .Select(m => (monitor: m, host: TryGetHost(m.url)))
                .Where(x => x.host is not null)
                .GroupBy(x => x.host!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.monitor.lastCheckedAt).First().monitor, StringComparer.OrdinalIgnoreCase));
    }

    private static async Task<Dictionary<string, ProjectRow>> LoadProjects(ProjectsDbContext db, CancellationToken ct)
        => await db.Projects.AsNoTracking()
            .Select(p => new ProjectRow(p.Id.ToString(), p.Name, p.Slug.Value, p.Color))
            .ToDictionaryAsync(p => p.id, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);

    private static async Task<Dictionary<string, TemplateRow>> LoadTemplates(ProjectsDbContext db, CancellationToken ct)
        => await db.Templates.AsNoTracking()
            .Select(t => new TemplateRow(
                t.Id.ToString(),
                t.ProjectId.ToString(),
                t.Name,
                t.Slug.Value,
                t.Source.GitRepoUrl.Value,
                t.Source.DefaultBranch))
            .ToDictionaryAsync(t => t.id, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);

    private static async Task<Dictionary<string, ClientRow>> LoadClients(ProjectsDbContext db, CancellationToken ct)
        => await db.Clients.AsNoTracking()
            .Select(c => new ClientRow(c.Id.ToString(), c.ProjectId.ToString(), c.Slug, c.DisplayName))
            .ToDictionaryAsync(c => c.id, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);

    private static async Task<Dictionary<string, InstanceRow>> LoadInstances(ProjectsDbContext db, CancellationToken ct)
    {
        var instances = await db.Instances.AsNoTracking()
            .Include(i => i.Ports)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var clients = await LoadClients(db, ct);
        return instances.ToDictionary(
            i => i.Id.ToString(),
            i => new InstanceRow(
                i.Id.ToString(),
                i.TemplateId.ToString(),
                i.ClientId.ToString(),
                clients.GetValueOrDefault(i.ClientId.ToString())?.slug ?? string.Empty,
                i.Environment,
                i.Slug,
                i.TargetVmId,
                i.ContainerName,
                i.Ports.Count > 0 ? i.Ports[0].ContainerPort.Value : null,
                i.CustomDomain,
                i.AutoHostname,
                i.TrackedRef,
                i.IsEphemeral,
                i.UpdatedAt),
            StringComparer.Ordinal);
    }

    private static async Task<Dictionary<string, VmRow>> LoadVms(VmsDbContext db, CancellationToken ct)
        => await db.Vms.AsNoTracking()
            .Select(v => new VmRow(
                v.Id.ToString(),
                v.Name,
                v.Slug.Value,
                v.Status.ToString(),
                v.AcceptsPreviews,
                v.LastConnectedAt,
                v.LastSeenAt,
                v.UpdatedAt))
            .ToDictionaryAsync(v => v.id, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);

    private static AppEnvironmentOverviewDto ToAppEnvironment(
        InstanceRow i,
        OpsSnapshot snapshot,
        IReadOnlyDictionary<string, VmRow> vms)
    {
        var template = snapshot.Templates.GetValueOrDefault(i.templateId);
        var project = template is null ? null : snapshot.Projects.GetValueOrDefault(template.projectId);
        var client = snapshot.Clients.GetValueOrDefault(i.clientId);
        var deployment = snapshot.DeploymentsByInstance.GetValueOrDefault(i.id);
        var monitor = snapshot.MonitorsByInstance.GetValueOrDefault(i.id);
        var vm = vms.GetValueOrDefault(i.targetVmId);

        var issues = 0;
        if (string.IsNullOrWhiteSpace(i.publicUrl))
        {
            issues++;
        }
        if (deployment is not null && IsFailed(deployment.status))
        {
            issues++;
        }
        if (monitor?.status == "Down")
        {
            issues++;
        }
        if (vm?.status == "Disconnected")
        {
            issues++;
        }

        var status = deployment is not null && IsFailed(deployment.status)
            ? "failed"
            : monitor?.status == "Down"
                ? "failed"
                : deployment is not null && IsActive(deployment.status)
                    ? "deploying"
                    : issues > 0
                        ? "degraded"
                        : "healthy";

        return new AppEnvironmentOverviewDto(
            i.id,
            i.slug,
            template?.id ?? i.templateId,
            template?.name ?? i.templateId,
            template?.slug ?? string.Empty,
            project?.id,
            project?.name ?? string.Empty,
            project?.slug ?? string.Empty,
            client?.id ?? i.clientId,
            client?.displayName ?? i.clientSlug,
            client?.slug ?? i.clientSlug,
            i.environment,
            i.targetVmId,
            vm?.name ?? i.targetVmId,
            vm?.status ?? "Unknown",
            i.publicUrl,
            i.trackedRef,
            deployment?.id,
            deployment?.status,
            deployment?.createdAt,
            monitor?.id,
            monitor?.status,
            status,
            issues,
            i.isEphemeral);
    }

    private static bool MatchesAppEnvironmentFilters(
        AppEnvironmentOverviewDto row,
        string? q,
        string? status,
        string? appId,
        string? environment,
        string? machineId)
    {
        if (!string.IsNullOrWhiteSpace(appId) && !string.Equals(row.AppId, appId, StringComparison.Ordinal))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(environment) && !string.Equals(row.Environment, environment, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(machineId) && !string.Equals(row.MachineId, machineId, StringComparison.Ordinal))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(row.HealthStatus, status, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(q))
        {
            return true;
        }

        return Contains(row.Slug, q)
            || Contains(row.AppName, q)
            || Contains(row.TenantName, q)
            || Contains(row.Environment, q)
            || Contains(row.MachineName, q)
            || Contains(row.PublicUrl, q)
            || Contains(row.TrackedRef, q);
    }

    private static bool MatchesReleaseFilters(
        ReleaseOverviewDto row,
        string? q,
        string? status,
        string? appId,
        string? gitRef)
    {
        if (!string.IsNullOrWhiteSpace(appId) && !string.Equals(row.AppId, appId, StringComparison.Ordinal))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(row.Status, status, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(gitRef) && !Contains(row.GitRef, gitRef))
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(q))
        {
            return true;
        }

        return Contains(row.AppName, q)
            || Contains(row.PortfolioName, q)
            || Contains(row.GitRef, q)
            || Contains(row.GitSha, q)
            || Contains(row.ShortSha, q)
            || Contains(row.Trigger, q)
            || Contains(row.TriggeredBy, q)
            || row.Targets.Any(t => Contains(t.TenantName, q) || Contains(t.Environment, q) || Contains(t.AppEnvironmentSlug, q));
    }

    private static EndpointOwnerDto? ResolveEndpointOwner(string hostname, IReadOnlyList<RouteRow> routes, OpsSnapshot snapshot)
    {
        var byHost = snapshot.Instances.FirstOrDefault(i =>
            string.Equals(i.customDomain, hostname, StringComparison.OrdinalIgnoreCase)
            || string.Equals(i.autoHostname, hostname, StringComparison.OrdinalIgnoreCase));
        var instance = byHost ?? snapshot.Instances.FirstOrDefault(i =>
            routes.Any(r => r.backendUrl.Contains($"://{i.slug}-", StringComparison.OrdinalIgnoreCase)));
        if (instance is null)
        {
            return null;
        }

        var template = snapshot.Templates.GetValueOrDefault(instance.templateId);
        var client = snapshot.Clients.GetValueOrDefault(instance.clientId);
        return new EndpointOwnerDto(
            instance.id,
            instance.slug,
            template?.id,
            template?.name,
            client?.id,
            client?.displayName ?? instance.clientSlug,
            instance.environment,
            instance.targetVmId);
    }

    private static PublicEndpointOverviewDto BuildPublicEndpoint(
        string hostname,
        IReadOnlyList<RouteRow> routeRows,
        OpsSnapshot snapshot,
        PublicAccessInfraSnapshot publicInfra)
    {
        var owner = ResolveEndpointOwner(hostname, routeRows, snapshot);
        var monitor = snapshot.MonitorsByUrlHost.GetValueOrDefault(hostname);
        var dnsRecord = FindDnsRecord(hostname, publicInfra);
        var dnsTargetConfigured = string.IsNullOrWhiteSpace(publicInfra.TunnelCname)
            || string.Equals(dnsRecord?.content, publicInfra.TunnelCname, StringComparison.OrdinalIgnoreCase);
        var tunnelConfigured = HasTunnelIngress(hostname, publicInfra);
        var routeDtos = routeRows.Select(r => new PublicEndpointRouteDto(r.id, r.pathPrefix, r.backendUrl)).ToList();
        var issues = new List<string>();
        if (owner is null)
        {
            issues.Add("route.owner_missing");
        }
        if (FindZoneForHostname(hostname, publicInfra) is null)
        {
            issues.Add("endpoint.dns_zone_missing");
        }
        if (dnsRecord is null)
        {
            issues.Add("endpoint.dns_missing");
        }
        if (dnsRecord is not null && !dnsTargetConfigured)
        {
            issues.Add("endpoint.dns_target_mismatch");
        }
        if (publicInfra.Tunnel is null)
        {
            issues.Add("endpoint.tunnel_missing");
        }
        if (publicInfra.Tunnel is not null && !tunnelConfigured)
        {
            issues.Add("endpoint.tunnel_ingress_missing");
        }
        if (monitor is null)
        {
            issues.Add("endpoint.monitor_missing");
        }
        if (monitor?.status == "Down")
        {
            issues.Add("monitor.down");
        }
        var health = issues.Count == 0
            ? "healthy"
            : issues.Any(i => i is "route.owner_missing" or "endpoint.dns_zone_missing" or "endpoint.dns_missing" or "endpoint.dns_target_mismatch" or "endpoint.tunnel_missing" or "endpoint.tunnel_ingress_missing" or "monitor.down")
                ? "broken"
                : "degraded";

        return new PublicEndpointOverviewDto(
            hostname,
            $"https://{hostname}",
            owner?.instanceId,
            owner?.instanceSlug,
            owner?.appId,
            owner?.appName,
            owner?.tenantId,
            owner?.tenantName,
            owner?.environment,
            owner?.machineId,
            owner is null ? "missing" : "resolved",
            health,
            dnsRecord is not null,
            dnsRecord?.content,
            publicInfra.TunnelCname,
            tunnelConfigured,
            publicInfra.Tunnel?.Name,
            routeRows.Any(r => r.tlsEnabled),
            monitor?.id,
            monitor?.status,
            issues,
            routeDtos);
    }

    private static bool MatchesPublicEndpointFilters(
        PublicEndpointOverviewDto row,
        string? q,
        string? health,
        string? appId,
        string? environment,
        string? dns,
        string? tunnel,
        string? monitor)
    {
        if (!string.IsNullOrWhiteSpace(health) && !string.Equals(row.HealthStatus, health, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(appId) && !string.Equals(row.AppId, appId, StringComparison.Ordinal))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(environment) && !string.Equals(row.Environment, environment, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (!MatchesDnsFilter(row, dns))
        {
            return false;
        }
        if (!MatchesTunnelFilter(row, tunnel))
        {
            return false;
        }
        if (!MatchesMonitorFilter(row, monitor))
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(q))
        {
            return true;
        }

        return Contains(row.Hostname, q)
            || Contains(row.AppName, q)
            || Contains(row.TenantName, q)
            || Contains(row.Environment, q)
            || Contains(row.DnsTarget, q)
            || Contains(row.ExpectedDnsTarget, q)
            || Contains(row.TunnelName, q)
            || row.Issues.Any(issue => Contains(issue, q))
            || row.Routes.Any(route => Contains(route.PathPrefix, q) || Contains(route.BackendUrl, q));
    }

    private static bool MatchesDnsFilter(PublicEndpointOverviewDto row, string? dns)
        => string.IsNullOrWhiteSpace(dns)
            || dns.Equals("ok", StringComparison.OrdinalIgnoreCase) && row.DnsConfigured && (row.ExpectedDnsTarget is null || string.Equals(row.DnsTarget, row.ExpectedDnsTarget, StringComparison.OrdinalIgnoreCase))
            || dns.Equals("missing", StringComparison.OrdinalIgnoreCase) && !row.DnsConfigured
            || dns.Equals("wrong", StringComparison.OrdinalIgnoreCase) && row.DnsConfigured && row.ExpectedDnsTarget is not null && !string.Equals(row.DnsTarget, row.ExpectedDnsTarget, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesTunnelFilter(PublicEndpointOverviewDto row, string? tunnel)
        => string.IsNullOrWhiteSpace(tunnel)
            || tunnel.Equals("ok", StringComparison.OrdinalIgnoreCase) && row.TunnelConfigured
            || tunnel.Equals("missing", StringComparison.OrdinalIgnoreCase) && !row.TunnelConfigured;

    private static bool MatchesMonitorFilter(PublicEndpointOverviewDto row, string? monitor)
        => string.IsNullOrWhiteSpace(monitor)
            || monitor.Equals("missing", StringComparison.OrdinalIgnoreCase) && row.MonitorId is null
            || monitor.Equals("down", StringComparison.OrdinalIgnoreCase) && string.Equals(row.MonitorStatus, "Down", StringComparison.OrdinalIgnoreCase)
            || monitor.Equals("up", StringComparison.OrdinalIgnoreCase) && string.Equals(row.MonitorStatus, "Up", StringComparison.OrdinalIgnoreCase);

    private static OperationalIssueDto Issue(string code, string severity, string title, string envId, string? appId, string? appName, string? tenantName, string env, DateTimeOffset? seenAt)
        => new(
            $"{envId}:{code}",
            code,
            severity,
            title,
            "AppEnvironment",
            envId,
            envId,
            appId,
            appName,
            tenantName,
            env,
            seenAt,
            SuggestedAppEnvironmentAction(code),
            $"/instances/{envId}");

    private static string SuggestedAppEnvironmentAction(string code)
        => code switch
        {
            "app_environment.no_public_url" => "Configure public access",
            "release.deploy_failed" => "Open deploy context",
            "monitor.down" => "Check monitor and endpoint",
            "machine.disconnected" => "Check machine",
            _ => "Open App Environment",
        };

    private static string SuggestedEndpointAction(string code, string? appEnvironmentId)
        => code switch
        {
            "route.owner_missing" => "Resolve endpoint owner",
            "endpoint.dns_zone_missing" => "Register DNS zone",
            "endpoint.dns_missing" or "endpoint.dns_target_mismatch" => appEnvironmentId is null ? "Resolve owner before DNS" : "Reconcile DNS",
            "endpoint.tunnel_missing" => "Register Cloudflare Tunnel",
            "endpoint.tunnel_ingress_missing" => appEnvironmentId is null ? "Resolve owner before tunnel" : "Reconcile tunnel",
            "endpoint.monitor_missing" => appEnvironmentId is null ? "Resolve owner before monitor" : "Create monitor",
            "monitor.down" => appEnvironmentId is null ? "Open public endpoint" : "Run monitor check",
            _ => appEnvironmentId is null ? "Open public endpoint" : "Reconcile public access",
        };

    private static bool MatchesOperationalIssueFilters(
        OperationalIssueDto row,
        string? q,
        string? severity,
        string? resourceType,
        string? appId)
    {
        if (!string.IsNullOrWhiteSpace(severity) && !string.Equals(row.Severity, severity, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(resourceType) && !string.Equals(row.ResourceType, resourceType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(appId) && !string.Equals(row.AppId, appId, StringComparison.Ordinal))
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(q))
        {
            return true;
        }

        return Contains(row.Code, q)
            || Contains(row.Title, q)
            || Contains(row.ResourceType, q)
            || Contains(row.ResourceId, q)
            || Contains(row.AppName, q)
            || Contains(row.TenantName, q)
            || Contains(row.Environment, q)
            || Contains(row.SuggestedAction, q);
    }

    private static bool MatchesMachineFilters(
        MachineOverviewDto row,
        string? q,
        string? readiness,
        bool? acceptsPreviews)
    {
        if (!string.IsNullOrWhiteSpace(readiness) && !string.Equals(row.ReadinessStatus, readiness, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (acceptsPreviews is not null && row.AcceptsPreviews != acceptsPreviews.Value)
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(q))
        {
            return true;
        }

        return Contains(row.Name, q)
            || Contains(row.Slug, q)
            || Contains(row.Status, q)
            || Contains(row.ReadinessStatus, q)
            || row.Workloads.Any(w => Contains(w.AppName, q) || Contains(w.TenantName, q) || Contains(w.Environment, q) || Contains(w.AppEnvironmentSlug, q));
    }

    private static bool MatchesDataServiceFilters(
        DataServiceOverviewDto row,
        string? q,
        string? status,
        string? type,
        string? appEnvironmentId)
    {
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(row.Status, status, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(type) && !string.Equals(row.Type, type, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(appEnvironmentId) && row.Bindings.All(b => !string.Equals(b.AppEnvironmentId, appEnvironmentId, StringComparison.Ordinal)))
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(q))
        {
            return true;
        }

        return Contains(row.Name, q)
            || Contains(row.Slug, q)
            || Contains(row.Type, q)
            || Contains(row.Status, q)
            || row.Bindings.Any(b => Contains(b.AppName, q) || Contains(b.TenantName, q) || Contains(b.Environment, q) || Contains(b.ResourceName, q));
    }

    private static string ResolveAggregateStatus(int failedCount, int activeCount, int issueCount)
        => failedCount > 0 ? "failed" : activeCount > 0 ? "active" : issueCount > 0 ? "degraded" : "healthy";

    private static bool IsFailed(string status)
        => status.Equals("Failed", StringComparison.OrdinalIgnoreCase)
           || status.Equals("RolledBack", StringComparison.OrdinalIgnoreCase);

    private static bool IsActive(string status)
        => status is "Queued" or "Cloning" or "Building" or "Pushing" or "Pending" or "Pulling" or "Starting" or "Healthcheck" or "Swapping";

    private static string ShortSha(string gitSha)
        => string.IsNullOrWhiteSpace(gitSha) ? string.Empty : gitSha[..Math.Min(gitSha.Length, 8)];

    private static string? TryGetHost(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;

    private static string? BuildBackendUrl(InstanceRow instance)
        => string.IsNullOrWhiteSpace(instance.containerName) || instance.primaryPort is null
            ? null
            : $"http://{instance.containerName}:{instance.primaryPort}";

    private static string SafeSlug(string hostname)
    {
        var chars = hostname.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray();
        var raw = new string(chars).Trim('-');
        if (raw.Length > 60)
        {
            raw = raw[..60];
        }

        return string.IsNullOrEmpty(raw) ? "monitor" : raw;
    }

    private static bool Contains(string? value, string query)
        => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    private static int SeverityRank(string severity)
        => severity switch
        {
            "critical" => 3,
            "warning" => 2,
            _ => 1,
        };

    private static string ResolveMachineReadiness(string status, int failingWorkloads, int deployingWorkloads)
    {
        if (status.Equals("Disconnected", StringComparison.OrdinalIgnoreCase))
        {
            return "offline";
        }
        if (failingWorkloads > 0)
        {
            return "degraded";
        }
        if (deployingWorkloads > 0)
        {
            return "busy";
        }
        if (status.Equals("Connected", StringComparison.OrdinalIgnoreCase))
        {
            return "ready";
        }
        return "unknown";
    }

    private static int ReadinessRank(string readiness)
        => readiness switch
        {
            "offline" => 0,
            "degraded" => 1,
            "unknown" => 2,
            "busy" => 3,
            _ => 4,
        };

    private sealed record OpsSnapshot(
        IReadOnlyDictionary<string, ProjectRow> Projects,
        IReadOnlyDictionary<string, TemplateRow> Templates,
        IReadOnlyDictionary<string, ClientRow> Clients,
        IReadOnlyList<InstanceRow> Instances,
        IReadOnlyDictionary<string, DeploymentRow> DeploymentsByInstance,
        IReadOnlyDictionary<string, MonitorRow> MonitorsByInstance,
        IReadOnlyDictionary<string, MonitorRow> MonitorsByUrlHost);

    private sealed record ProjectRow(string id, string name, string slug, string? color);
    private sealed record TemplateRow(string id, string projectId, string name, string slug, string gitRepoUrl, string defaultBranch);
    private sealed record ClientRow(string id, string projectId, string slug, string displayName);
    private sealed record InstanceRow(string id, string templateId, string clientId, string clientSlug, string environment, string slug, string targetVmId, string containerName, int? primaryPort, string? customDomain, string? autoHostname, string? trackedRef, bool isEphemeral, DateTimeOffset updatedAt)
    {
        public string? publicUrl => customDomain is { Length: > 0 } ? $"https://{customDomain}" : autoHostname is { Length: > 0 } ? $"https://{autoHostname}" : null;
    }
    private sealed record DeploymentRow(string id, string buildId, string instanceId, string status, DateTimeOffset createdAt, DateTimeOffset? startedAt, DateTimeOffset? finishedAt, string newImageRef, string? errorCode, string? errorMessage);
    private sealed record MonitorRow(string id, string name, string url, string? instanceId, string? projectId, bool enabled, string status, DateTimeOffset? lastCheckedAt);
    private sealed record VmRow(string id, string name, string slug, string status, bool acceptsPreviews, DateTimeOffset? lastConnectedAt, DateTimeOffset? lastSeenAt, DateTimeOffset updatedAt);
    private sealed record RouteRow(string id, string hostname, string pathPrefix, string backendUrl, bool tlsEnabled);
    private sealed record EndpointOwnerDto(string instanceId, string instanceSlug, string? appId, string? appName, string? tenantId, string? tenantName, string environment, string machineId);
    private sealed record AppliedResource(string? resourceId, string? errorCode, string? errorMessage);

    // Snapshot de la infra pública (Cloudflare) usado por el reconcile de Public Access.
    private sealed record DnsRecordRow(
        string id, string zoneId, string zoneName, string type, string name,
        string content, bool proxied, bool synced, string? lastError);
    private sealed record PublicAccessInfraSnapshot(
        string? TunnelCname,
        IReadOnlyList<CloudflareZoneDto> Zones,
        IReadOnlyList<DnsRecordRow> DnsRecords,
        CloudflareTunnelDto? Tunnel);

    public sealed record AppOverviewDto(
        string Id,
        string Name,
        string Slug,
        string GitRepoUrl,
        string DefaultBranch,
        string PortfolioId,
        string PortfolioName,
        string PortfolioSlug,
        int TenantCount,
        IReadOnlyList<string> Environments,
        int AppEnvironmentCount,
        string Status,
        string? LatestReleaseId,
        DateTimeOffset? LatestReleaseAt,
        int IssueCount);

    public sealed record AppEnvironmentOverviewDto(
        string Id,
        string Slug,
        string AppId,
        string AppName,
        string AppSlug,
        string? PortfolioId,
        string PortfolioName,
        string PortfolioSlug,
        string TenantId,
        string TenantName,
        string TenantSlug,
        string Environment,
        string MachineId,
        string MachineName,
        string MachineStatus,
        string? PublicUrl,
        string? TrackedRef,
        string? LatestReleaseId,
        string? LatestReleaseStatus,
        DateTimeOffset? LatestReleaseAt,
        string? MonitorId,
        string? MonitorStatus,
        string HealthStatus,
        int IssueCount,
        bool IsEphemeral);

    public sealed record ReleaseOverviewDto(
        string Id,
        string BuildId,
        string? AppId,
        string AppName,
        string AppSlug,
        string? PortfolioId,
        string PortfolioName,
        string GitSha,
        string ShortSha,
        string GitRef,
        string Trigger,
        string? TriggeredBy,
        string Status,
        string BuildStatus,
        int TargetCount,
        int CompletedCount,
        int FailedCount,
        int ActiveCount,
        DateTimeOffset CreatedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? FinishedAt,
        string? ImageRef,
        string? ErrorCode,
        string? ErrorMessage,
        IReadOnlyList<ReleaseTargetDto> Targets);

    public sealed record ReleaseTargetDto(
        string DeploymentId,
        string AppEnvironmentId,
        string AppEnvironmentSlug,
        string? TenantId,
        string TenantName,
        string Environment,
        string Status,
        string? ErrorCode,
        string? ErrorMessage);

    public sealed record PublicEndpointOverviewDto(
        string Hostname,
        string Url,
        string? AppEnvironmentId,
        string? AppEnvironmentSlug,
        string? AppId,
        string? AppName,
        string? TenantId,
        string? TenantName,
        string? Environment,
        string? MachineId,
        string OwnerStatus,
        string HealthStatus,
        bool DnsConfigured,
        string? DnsTarget,
        string? ExpectedDnsTarget,
        bool TunnelConfigured,
        string? TunnelName,
        bool TlsEnabled,
        string? MonitorId,
        string? MonitorStatus,
        IReadOnlyList<string> Issues,
        IReadOnlyList<PublicEndpointRouteDto> Routes);

    public sealed record PublicEndpointRouteDto(string RouteId, string PathPrefix, string BackendUrl);

    public sealed record PublicAccessStateDto(
        string AppEnvironmentId,
        string AppEnvironmentSlug,
        string? AppId,
        string? AppName,
        string? TenantId,
        string? TenantName,
        string Environment,
        string? DesiredHostname,
        string? DesiredUrl,
        string DesiredSource,
        string HealthStatus,
        string NextAction,
        bool DnsConfigured,
        string? DnsTarget,
        string? ExpectedDnsTarget,
        bool TunnelConfigured,
        string? TunnelName,
        bool RouteConfigured,
        bool TlsEnabled,
        bool MonitorConfigured,
        string? MonitorStatus,
        IReadOnlyList<PublicEndpointRouteDto> Routes,
        IReadOnlyList<string> Issues);

    public sealed record PublicAccessReconcileRequest(bool? DryRun);

    public sealed record PublicAccessReconcileResultDto(
        string AppEnvironmentId,
        bool DryRun,
        bool Applied,
        IReadOnlyList<PublicAccessReconcileActionDto> Actions,
        PublicAccessStateDto State);

    public sealed record PublicAccessReconcileActionDto(
        string Kind,
        string Status,
        string Message,
        string? ResourceId,
        string? ErrorCode,
        string? ErrorMessage);

    public sealed record MachineOverviewDto(
        string Id,
        string Name,
        string Slug,
        string Status,
        string ReadinessStatus,
        int AppEnvironmentCount,
        int FailingAppEnvironmentCount,
        int DeployingAppEnvironmentCount,
        int PreviewAppEnvironmentCount,
        bool AcceptsPreviews,
        DateTimeOffset? LastConnectedAt,
        DateTimeOffset? LastSeenAt,
        DateTimeOffset UpdatedAt,
        IReadOnlyList<MachineWorkloadDto> Workloads);

    public sealed record MachineWorkloadDto(
        string AppEnvironmentId,
        string AppEnvironmentSlug,
        string AppId,
        string AppName,
        string TenantId,
        string TenantName,
        string Environment,
        string HealthStatus,
        string? LatestReleaseStatus,
        string? PublicUrl,
        int IssueCount,
        bool IsEphemeral);

    public sealed record DataServiceOverviewDto(
        string Id,
        string Slug,
        string Name,
        string Type,
        string Version,
        string Status,
        string MachineId,
        string ContainerName,
        bool ExposedExternally,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? ProvisionedAt,
        DateTimeOffset? LastBackupAt,
        DateTimeOffset? LastRestoredAt,
        string? ErrorCode,
        string? ErrorMessage,
        int ActiveBindingCount,
        IReadOnlyList<DataServiceBindingOverviewDto> Bindings);

    public sealed record DataServiceBindingOverviewDto(
        string Id,
        string AppEnvironmentId,
        string? AppEnvironmentSlug,
        string? AppId,
        string? AppName,
        string? PortfolioId,
        string? PortfolioName,
        string? TenantId,
        string? TenantName,
        string? Environment,
        string ResourceName,
        string Permissions,
        string EnvVarPrefix,
        bool HasMigrationsHook,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ProvisionedAt,
        DateTimeOffset? RevokedAt,
        DateTimeOffset? LastRotatedAt);

    public sealed record OperationalIssueDto(
        string Id,
        string Code,
        string Severity,
        string Title,
        string ResourceType,
        string ResourceId,
        string? AppEnvironmentId,
        string? AppId,
        string? AppName,
        string? TenantName,
        string? Environment,
        DateTimeOffset? LastSeenAt,
        string SuggestedAction,
        string? SuggestedHref);
}
