using Aethra.Modules.Deployments.Infrastructure;
using Aethra.Modules.Deployments.Domain.Build;
using Aethra.Modules.Monitoring.Infrastructure;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Proxy.Infrastructure;
using Aethra.Modules.Vms.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

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

        group.MapGet("/public-endpoints", ListPublicEndpoints)
            .RequireAuthorization("scope:proxy:read")
            .WithName("ListOperationalPublicEndpoints");

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
            .OrderBy(r => r.AppName)
            .ThenBy(r => r.TenantName)
            .ThenBy(r => r.Environment)
            .ToList();

        return Results.Ok(rows);
    }

    private static async Task<IResult> ListReleases(
        ProjectsDbContext projectsDb,
        DeploymentsDbContext deploymentsDb,
        CancellationToken ct)
    {
        var projects = await LoadProjects(projectsDb, ct);
        var templates = await LoadTemplates(projectsDb, ct);
        var instances = await LoadInstances(projectsDb, ct);
        var clients = await LoadClients(projectsDb, ct);

        var builds = await deploymentsDb.Builds.AsNoTracking()
            .OrderByDescending(b => b.CreatedAt)
            .Take(100)
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

        return Results.Ok(releases);
    }

    private static async Task<IResult> ListPublicEndpoints(
        ProjectsDbContext projectsDb,
        ProxyDbContext proxyDb,
        MonitoringDbContext monitoringDb,
        CancellationToken ct)
    {
        var snapshot = await LoadSnapshot(projectsDb, null, monitoringDb, ct);
        var routes = await proxyDb.Routes.AsNoTracking()
            .OrderBy(r => r.Hostname.Value)
            .ThenBy(r => r.PathPrefix)
            .Select(r => new RouteRow(r.Id.ToString(), r.Hostname.Value, r.PathPrefix, r.BackendUrl, r.TlsEnabled))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var groups = routes
            .GroupBy(r => r.hostname, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                return BuildPublicEndpoint(g.Key, g.ToList(), snapshot);
            })
            .OrderBy(e => e.HealthStatus == "healthy")
            .ThenBy(e => e.Hostname)
            .ToList();

        return Results.Ok(groups);
    }

    private static async Task<IResult> ListOperationalIssues(
        ProjectsDbContext projectsDb,
        DeploymentsDbContext deploymentsDb,
        ProxyDbContext proxyDb,
        MonitoringDbContext monitoringDb,
        VmsDbContext vmsDb,
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
                issues.Add(Issue("app_environment.no_public_url", "warning", "App Environment has no public URL", env.id, app?.name, client?.displayName, env.environment, env.updatedAt));
            }
            if (latestDeployment is { } d && IsFailed(d.status))
            {
                issues.Add(Issue("release.deploy_failed", "critical", d.errorMessage ?? "Deployment failed", env.id, app?.name, client?.displayName, env.environment, d.finishedAt ?? d.createdAt));
            }
            if (monitor?.status == "Down")
            {
                issues.Add(Issue("monitor.down", "critical", $"Monitor down: {monitor.name}", env.id, app?.name, client?.displayName, env.environment, monitor.lastCheckedAt ?? env.updatedAt));
            }
            if (vm?.status == "Disconnected")
            {
                issues.Add(Issue("machine.disconnected", "critical", $"Machine disconnected: {vm.name}", env.id, app?.name, client?.displayName, env.environment, vm.updatedAt));
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
                app?.name,
                null,
                null,
                build.at));
        }

        var routes = await proxyDb.Routes.AsNoTracking()
            .Select(r => new RouteRow(r.Id.ToString(), r.Hostname.Value, r.PathPrefix, r.BackendUrl, r.TlsEnabled))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var group in routes.GroupBy(r => r.hostname, StringComparer.OrdinalIgnoreCase))
        {
            var endpoint = BuildPublicEndpoint(group.Key, group.ToList(), snapshot);
            foreach (var code in endpoint.Issues)
            {
                issues.Add(new OperationalIssueDto(
                    $"endpoint:{endpoint.Hostname}:{code}",
                    code,
                    code == "route.owner_missing" || code == "monitor.down" ? "critical" : "warning",
                    $"{endpoint.Hostname}: {code}",
                    "PublicEndpoint",
                    endpoint.Hostname,
                    endpoint.AppEnvironmentId,
                    endpoint.AppName,
                    endpoint.TenantName,
                    endpoint.Environment,
                    null));
            }
        }

        return Results.Ok(issues.OrderByDescending(i => SeverityRank(i.Severity)).ThenBy(i => i.Code).ToList());
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
            .Select(i => new
            {
                id = i.Id.ToString(),
                templateId = i.TemplateId.ToString(),
                clientId = i.ClientId.ToString(),
                environment = i.Environment,
                slug = i.Slug,
                targetVmId = i.TargetVmId,
                containerName = i.ContainerName,
                customDomain = i.CustomDomain,
                autoHostname = i.AutoHostname,
                trackedRef = i.TrackedRef,
                isEphemeral = i.IsEphemeral,
                updatedAt = i.UpdatedAt
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var clients = await LoadClients(db, ct);
        return instances.ToDictionary(
            i => i.id,
            i => new InstanceRow(
                i.id,
                i.templateId,
                i.clientId,
                clients.GetValueOrDefault(i.clientId)?.slug ?? string.Empty,
                i.environment,
                i.slug,
                i.targetVmId,
                i.containerName,
                i.customDomain,
                i.autoHostname,
                i.trackedRef,
                i.isEphemeral,
                i.updatedAt),
            StringComparer.Ordinal);
    }

    private static async Task<Dictionary<string, VmRow>> LoadVms(VmsDbContext db, CancellationToken ct)
        => await db.Vms.AsNoTracking()
            .Select(v => new VmRow(v.Id.ToString(), v.Name, v.Slug.Value, v.Status.ToString(), v.UpdatedAt))
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

    private static PublicEndpointOverviewDto BuildPublicEndpoint(string hostname, IReadOnlyList<RouteRow> routeRows, OpsSnapshot snapshot)
    {
        var owner = ResolveEndpointOwner(hostname, routeRows, snapshot);
        var monitor = snapshot.MonitorsByUrlHost.GetValueOrDefault(hostname);
        var routeDtos = routeRows.Select(r => new PublicEndpointRouteDto(r.id, r.pathPrefix, r.backendUrl)).ToList();
        var issues = new List<string>();
        if (owner is null)
        {
            issues.Add("route.owner_missing");
        }
        if (monitor is null)
        {
            issues.Add("endpoint.monitor_missing");
        }
        if (monitor?.status == "Down")
        {
            issues.Add("monitor.down");
        }
        var health = issues.Count == 0 ? "healthy" : issues.Any(i => i is "route.owner_missing" or "monitor.down") ? "broken" : "degraded";

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
            routeRows.Any(r => r.tlsEnabled),
            monitor?.id,
            monitor?.status,
            issues,
            routeDtos);
    }

    private static OperationalIssueDto Issue(string code, string severity, string title, string envId, string? appName, string? tenantName, string env, DateTimeOffset? seenAt)
        => new($"{envId}:{code}", code, severity, title, "AppEnvironment", envId, envId, appName, tenantName, env, seenAt);

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

    private static int SeverityRank(string severity)
        => severity switch
        {
            "critical" => 3,
            "warning" => 2,
            _ => 1,
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
    private sealed record InstanceRow(string id, string templateId, string clientId, string clientSlug, string environment, string slug, string targetVmId, string containerName, string? customDomain, string? autoHostname, string? trackedRef, bool isEphemeral, DateTimeOffset updatedAt)
    {
        public string? publicUrl => customDomain is { Length: > 0 } ? $"https://{customDomain}" : autoHostname is { Length: > 0 } ? $"https://{autoHostname}" : null;
    }
    private sealed record DeploymentRow(string id, string buildId, string instanceId, string status, DateTimeOffset createdAt, DateTimeOffset? startedAt, DateTimeOffset? finishedAt, string newImageRef, string? errorCode, string? errorMessage);
    private sealed record MonitorRow(string id, string name, string url, string? instanceId, string? projectId, bool enabled, string status, DateTimeOffset? lastCheckedAt);
    private sealed record VmRow(string id, string name, string slug, string status, DateTimeOffset updatedAt);
    private sealed record RouteRow(string id, string hostname, string pathPrefix, string backendUrl, bool tlsEnabled);
    private sealed record EndpointOwnerDto(string instanceId, string instanceSlug, string? appId, string? appName, string? tenantId, string? tenantName, string environment, string machineId);

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
        bool TlsEnabled,
        string? MonitorId,
        string? MonitorStatus,
        IReadOnlyList<string> Issues,
        IReadOnlyList<PublicEndpointRouteDto> Routes);

    public sealed record PublicEndpointRouteDto(string RouteId, string PathPrefix, string BackendUrl);

    public sealed record OperationalIssueDto(
        string Id,
        string Code,
        string Severity,
        string Title,
        string ResourceType,
        string ResourceId,
        string? AppEnvironmentId,
        string? AppName,
        string? TenantName,
        string? Environment,
        DateTimeOffset? LastSeenAt);
}
