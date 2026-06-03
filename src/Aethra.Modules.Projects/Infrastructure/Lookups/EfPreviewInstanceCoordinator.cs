using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Domain.Clients;
using Aethra.Modules.Projects.Domain.Instances;
using Aethra.Modules.Projects.Domain.Templates;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Contracts.Settings;
using Aethra.Shared.Contracts.Vms;
using Aethra.Shared.Infrastructure.Outbox;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.Infrastructure.Lookups;

/// <summary>
/// F12.3 — Implementación EF de <see cref="IPreviewInstanceCoordinator"/>. Vive en
/// <c>Modules.Projects.Infrastructure</c> porque tiene que tocar los aggregates Project, Client,
/// Instance, Template — privados al módulo. El módulo Deployments lo consume vía el contrato
/// público en <c>Shared.Contracts.Projects</c>.
/// </summary>
internal sealed class EfPreviewInstanceCoordinator(
    ProjectsDbContext db,
    IPreviewVmPool vmPool,
    IBaseDomainProvider baseDomainProvider,
    IClock clock,
    IOutboxWriter<ProjectsDbContext> outbox) : IPreviewInstanceCoordinator
{
    /// <summary>
    /// Slug del Client interno usado para hospedar Instances ephemerals. Cumple el regex de Client
    /// (no admite <c>__</c>); convención: dentro de cada Project es único.
    /// </summary>
    private const string PreviewClientSlug = "preview";
    private const string PreviewEnvironment = "preview";

    public async Task<PreviewProvisioningResult> EnsurePreviewAsync(
        string templateId,
        int prNumber,
        string headSha,
        string? createdByUserId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(templateId);
        ArgumentNullException.ThrowIfNull(headSha);

        // 1) Cargar Template (incluye EnvironmentMapping por completitud aunque preview ignora cascade).
        if (!AethraId.TryParse(templateId, out var parsedTpl) || parsedTpl.Value.Prefix != "tpl")
        {
            return new PreviewProvisioningResult(PreviewProvisioningStatus.TemplateNotFound, null, null);
        }
        var typedTplId = new TemplateId(parsedTpl.Value);
        var template = await db.Templates
            .Include(t => t.EnvironmentMapping)
            .FirstOrDefaultAsync(t => t.Id == typedTplId, ct)
            .ConfigureAwait(false);
        if (template is null)
        {
            return new PreviewProvisioningResult(PreviewProvisioningStatus.TemplateNotFound, null, null);
        }

        // 2) Idempotencia: ¿ya existe Instance ephemeral para este templateId + prNumber?
        var trackedRef = $"refs/pull/{prNumber}/head";
        var existing = await db.Instances
            .FirstOrDefaultAsync(i => i.TemplateId == typedTplId
                && i.IsEphemeral
                && i.TrackedRef == trackedRef, ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return new PreviewProvisioningResult(
                PreviewProvisioningStatus.Reused,
                existing.Id.ToString(),
                existing.CustomDomain ?? existing.AutoHostname);
        }

        // 3) Cargar Project + cap.
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == template.ProjectId, ct).ConfigureAwait(false);
        if (project is null)
        {
            return new PreviewProvisioningResult(PreviewProvisioningStatus.TemplateNotFound, null, null);
        }
        if (project.PreviewMaxConcurrent <= 0)
        {
            return new PreviewProvisioningResult(
                PreviewProvisioningStatus.PreviewsDisabled,
                null, null,
                QuotaActual: 0,
                QuotaMax: project.PreviewMaxConcurrent);
        }

        // 4) Quota check — contamos Instances ephemerals del Project (via Template.ProjectId).
        var projectTemplateIds = await db.Templates
            .AsNoTracking()
            .Where(t => t.ProjectId == project.Id)
            .Select(t => t.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var currentPreviews = await db.Instances
            .AsNoTracking()
            .Where(i => i.IsEphemeral && projectTemplateIds.Contains(i.TemplateId))
            .CountAsync(ct)
            .ConfigureAwait(false);
        if (currentPreviews >= project.PreviewMaxConcurrent)
        {
            return new PreviewProvisioningResult(
                PreviewProvisioningStatus.QuotaExceeded,
                null, null,
                QuotaActual: currentPreviews,
                QuotaMax: project.PreviewMaxConcurrent);
        }

        // 5) Lazy-create del Client __preview__ (slug "preview" en este Project).
        Client previewClient;
        if (project.PreviewClientId is not null
            && AethraId.TryParse(project.PreviewClientId, out var existingCliRaw)
            && existingCliRaw.Value.Prefix == "cli")
        {
            var typedCliId = new ClientId(existingCliRaw.Value);
            previewClient = await db.Clients.FirstOrDefaultAsync(c => c.Id == typedCliId, ct).ConfigureAwait(false)
                ?? CreatePreviewClient(project);
        }
        else
        {
            previewClient = await db.Clients
                .FirstOrDefaultAsync(c => c.ProjectId == project.Id && c.Slug == PreviewClientSlug, ct)
                .ConfigureAwait(false) ?? CreatePreviewClient(project);
        }
        if (previewClient.Id != default && project.PreviewClientId != previewClient.Id.ToString())
        {
            project.AttachPreviewClient(previewClient.Id.ToString(), clock.UtcNow);
        }

        // 6) Pick round-robin VM con AcceptsPreviews=true.
        var pool = await vmPool.ListAvailableVmIdsAsync(ct).ConfigureAwait(false);
        if (pool.Count == 0)
        {
            return new PreviewProvisioningResult(PreviewProvisioningStatus.NoVmAvailable, null, null);
        }
        // Round-robin determinístico: index = currentPreviews % pool.Count. Suficiente para spread
        // razonable; no aspira a balance estricto de carga (eso vendría con métricas si hace falta).
        var targetVmId = pool[currentPreviews % pool.Count];

        // 7) Crear Instance ephemeral.
        var slug = $"pr-{prNumber}";
        Instance instance;
        try
        {
            instance = Instance.Create(
                templateId: typedTplId,
                clientId: previewClient.Id,
                environment: PreviewEnvironment,
                targetVmId: targetVmId,
                templateSlug: template.Slug.Value,
                clientSlug: previewClient.Slug,
                ports: null,
                volumes: null,
                healthcheck: null,
                autoDeployOnNewBuild: true,
                now: clock.UtcNow,
                slugOverride: slug,
                trackedRef: trackedRef,
                isEphemeral: true,
                expiresAt: null,
                createdByUserId: createdByUserId);
        }
        catch (ArgumentException)
        {
            return new PreviewProvisioningResult(PreviewProvisioningStatus.TemplateNotFound, null, null);
        }

        // 8) Auto-hostname si hay BaseDomain.
        var baseDomain = await baseDomainProvider.GetActiveAsync(ct).ConfigureAwait(false);
        string? hostname = null;
        if (baseDomain is not null)
        {
            hostname = $"{template.Slug.Value}-{previewClient.Slug}-{instance.Slug}.{baseDomain.Hostname}";
            instance.SetAutoHostname(hostname, clock.UtcNow);
        }

        db.Instances.Add(instance);

        // 9) Outbox: InstanceProvisioned event (Proxy crea Route, Cloudflare crea DNS si custom).
        await outbox.EnqueueAsync(new InstanceProvisionedIntegrationEvent(
            InstanceId: instance.Id.ToString(),
            TemplateId: instance.TemplateId.ToString(),
            ClientId: instance.ClientId.ToString(),
            Environment: instance.Environment,
            TargetVmId: instance.TargetVmId,
            ContainerName: instance.ContainerName,
            PrimaryPort: null,
            AutoHostname: instance.AutoHostname,
            CustomDomain: instance.CustomDomain,
            CreatedAt: instance.CreatedAt), ct).ConfigureAwait(false);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new PreviewProvisioningResult(
            PreviewProvisioningStatus.Created,
            instance.Id.ToString(),
            hostname);
    }

    public async Task<PreviewTeardownResult> TeardownPreviewAsync(
        string templateId, int prNumber, CancellationToken ct)
    {
        if (!AethraId.TryParse(templateId, out var parsed) || parsed.Value.Prefix != "tpl")
        {
            return new PreviewTeardownResult(PreviewTeardownStatus.NotFound, null);
        }
        var typedId = new TemplateId(parsed.Value);
        var trackedRef = $"refs/pull/{prNumber}/head";
        var instance = await db.Instances
            .FirstOrDefaultAsync(i => i.TemplateId == typedId
                && i.IsEphemeral
                && i.TrackedRef == trackedRef, ct)
            .ConfigureAwait(false);
        if (instance is null)
        {
            return new PreviewTeardownResult(PreviewTeardownStatus.NotFound, null);
        }

        var removedId = instance.Id.ToString();
        // Emitir event de remove para que Proxy/Containers limpien antes de borrar la fila.
        await outbox.EnqueueAsync(new InstanceRemovedIntegrationEvent(
            InstanceId: removedId,
            AutoHostname: instance.AutoHostname,
            CustomDomain: instance.CustomDomain,
            RemovedAt: clock.UtcNow), ct).ConfigureAwait(false);

        db.Instances.Remove(instance);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new PreviewTeardownResult(PreviewTeardownStatus.Removed, removedId);
    }

    public async Task<int> CountActivePreviewsForProjectAsync(string projectId, CancellationToken ct)
    {
        if (!AethraId.TryParse(projectId, out var parsed) || parsed.Value.Prefix != "prj")
        {
            return 0;
        }
        var typedId = new ProjectId(parsed.Value);
        var templateIds = await db.Templates
            .AsNoTracking()
            .Where(t => t.ProjectId == typedId)
            .Select(t => t.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return await db.Instances
            .AsNoTracking()
            .Where(i => i.IsEphemeral && templateIds.Contains(i.TemplateId))
            .CountAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<int> GetPreviewQuotaAsync(string projectId, CancellationToken ct)
    {
        if (!AethraId.TryParse(projectId, out var parsed) || parsed.Value.Prefix != "prj")
        {
            return 0;
        }
        var typedId = new ProjectId(parsed.Value);
        var project = await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == typedId)
            .Select(p => new { p.PreviewMaxConcurrent })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return project?.PreviewMaxConcurrent ?? 0;
    }

    private Client CreatePreviewClient(Project project)
    {
        var client = Client.Create(
            projectId: project.Id,
            slug: PreviewClientSlug,
            displayName: "Aethra Previews",
            now: clock.UtcNow,
            description: "Tenant interno auto-generado para Instances ephemerals de PR previews.",
            contactEmail: null,
            billingTag: null);
        db.Clients.Add(client);
        return client;
    }
}
