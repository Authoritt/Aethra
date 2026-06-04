using Aethra.Modules.Projects.Domain.Instances;
using Aethra.Modules.Projects.Domain.Templates;
using Aethra.Shared.Contracts.Projects;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.Infrastructure.Lookups;

/// <summary>
/// Implementación EF Core de <see cref="IInstanceLookup"/>. Lee Instances de
/// <see cref="ProjectsDbContext"/> y resuelve el <c>ProjectId</c> derivado del Template.
///
/// <para>
/// El <c>ProjectId</c> NO se persiste en la fila <c>instances</c> — se navega via Template.
/// Para evitar N+1 las queries hacen un join in-memory sobre el set de Templates necesarios
/// y resuelven el primer <see cref="Instance.Ports"/> como <c>PrimaryContainerPort</c> para
/// el routing.
/// </para>
/// </summary>
internal sealed class EfInstanceLookup(ProjectsDbContext db) : IInstanceLookup
{
    public async Task<InstanceForDeployView?> GetByIdAsync(string instanceId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(instanceId);
        // EF no traduce `Id.ToString() == arg` con ValueConverter activo. Materializamos.
        var all = await db.Instances.AsNoTracking().Include(i => i.Ports).ToListAsync(ct).ConfigureAwait(false);
        var inst = all.FirstOrDefault(i => i.Id.ToString() == instanceId);
        if (inst is null)
        {
            return null;
        }
        var projectId = await ResolveProjectIdAsync(inst.TemplateId, ct).ConfigureAwait(false);
        return Project(inst, projectId);
    }

    public async Task<IReadOnlyList<InstanceForDeployView>> FindByTemplateAsync(
        string templateId, bool autoDeployOnly, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(templateId);
        var allList = await db.Instances.AsNoTracking().Include(i => i.Ports)
            .ToListAsync(ct).ConfigureAwait(false);
        IEnumerable<Instance> query = allList.Where(i => i.TemplateId.ToString() == templateId);
        if (autoDeployOnly)
        {
            query = query.Where(i => i.AutoDeployOnNewBuild);
        }
        var list = query.ToList();
        if (list.Count == 0)
        {
            return Array.Empty<InstanceForDeployView>();
        }
        // Todos comparten TemplateId ⇒ resolvemos ProjectId una sola vez.
        var projectId = await ResolveProjectIdAsync(list[0].TemplateId, ct).ConfigureAwait(false);
        return list.Select(i => Project(i, projectId)).ToList();
    }

    /// <summary>
    /// F12.3 — resuelve el efectivo <c>TrackedRef</c> de cada Instance del Template y devuelve
    /// solo las que matchean <paramref name="gitRef"/>. Como la resolución depende del Template
    /// (cascade EnvironmentMapping → DefaultBranch), carga el Template una sola vez y aplica la
    /// función estática <see cref="Instance.ResolveTrackedRef"/>.
    /// </summary>
    public async Task<IReadOnlyList<InstanceForDeployView>> FindByTrackedRefAsync(
        string templateId, string gitRef, bool autoDeployOnly, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(templateId);
        ArgumentNullException.ThrowIfNull(gitRef);

        // El Id se persiste con value converter → EF no traduce `t.Id.ToString() == arg`.
        // Materializamos y filtramos en memoria (cardinalidad de Templates muy baja).
        var allTemplates = await db.Templates
            .AsNoTracking()
            .Include(t => t.EnvironmentMapping)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var template = allTemplates.FirstOrDefault(t => t.Id.ToString() == templateId);
        if (template is null)
        {
            return Array.Empty<InstanceForDeployView>();
        }

        var allList = await db.Instances.AsNoTracking().Include(i => i.Ports)
            .ToListAsync(ct).ConfigureAwait(false);
        IEnumerable<Instance> query = allList.Where(i => i.TemplateId.ToString() == templateId);
        if (autoDeployOnly)
        {
            query = query.Where(i => i.AutoDeployOnNewBuild);
        }
        var filtered = query
            .Where(i => string.Equals(i.ResolveTrackedRef(template), gitRef, StringComparison.Ordinal))
            .ToList();
        if (filtered.Count == 0)
        {
            return Array.Empty<InstanceForDeployView>();
        }
        var projectId = template.ProjectId.ToString();
        return filtered.Select(i => Project(i, projectId)).ToList();
    }

    public async Task<IReadOnlyList<InstanceForDeployView>> FindByClientAsync(
        string clientId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(clientId);
        var all = await db.Instances.AsNoTracking().Include(i => i.Ports)
            .ToListAsync(ct).ConfigureAwait(false);
        var list = all.Where(i => i.ClientId.ToString() == clientId).ToList();
        if (list.Count == 0)
        {
            return Array.Empty<InstanceForDeployView>();
        }
        // Diferentes Instances pueden venir de Templates distintos: resolvemos ProjectId por
        // cada TemplateId distinto en una sola query batch.
        var templateIds = list.Select(i => i.TemplateId).Distinct().ToList();
        var templates = await db.Templates
            .AsNoTracking()
            .Where(t => templateIds.Contains(t.Id))
            .Select(t => new { t.Id, t.ProjectId })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var lookup = templates.ToDictionary(x => x.Id, x => x.ProjectId.ToString());

        var result = new List<InstanceForDeployView>(list.Count);
        foreach (var i in list)
        {
            var projectId = lookup.TryGetValue(i.TemplateId, out var pid) ? pid : string.Empty;
            result.Add(Project(i, projectId));
        }
        return result;
    }

    private async Task<string> ResolveProjectIdAsync(TemplateId templateId, CancellationToken ct)
    {
        var pid = await db.Templates
            .AsNoTracking()
            .Where(t => t.Id == templateId)
            .Select(t => t.ProjectId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return pid.ToString();
    }

    private static InstanceForDeployView Project(Instance i, string projectId)
    {
        // PrimaryContainerPort: el primer mapeo de la lista (orden de inserción). Si no hay
        // puertos, queda null — el deploy lo trata como contenedor headless.
        int? primaryPort = i.Ports.Count > 0 ? i.Ports[0].ContainerPort.Value : null;
        return new InstanceForDeployView(
            InstanceId: i.Id.ToString(),
            TemplateId: i.TemplateId.ToString(),
            ClientId: i.ClientId.ToString(),
            ProjectId: projectId,
            Slug: i.Slug,
            Environment: i.Environment,
            TargetVmId: i.TargetVmId,
            ContainerName: i.ContainerName,
            AutoDeployOnNewBuild: i.AutoDeployOnNewBuild,
            CustomDomain: i.CustomDomain,
            AutoHostname: i.AutoHostname,
            PrimaryContainerPort: primaryPort,
            TrackedRef: i.TrackedRef,
            IsEphemeral: i.IsEphemeral,
            CreatedByUserId: i.CreatedByUserId);
    }
}
