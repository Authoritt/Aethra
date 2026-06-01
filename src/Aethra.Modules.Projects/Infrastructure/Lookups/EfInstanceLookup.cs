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
        var inst = await db.Instances
            .AsNoTracking()
            .Include(i => i.Ports)
            .FirstOrDefaultAsync(i => i.Id.ToString() == instanceId, ct)
            .ConfigureAwait(false);
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
        var query = db.Instances
            .AsNoTracking()
            .Include(i => i.Ports)
            .Where(i => i.TemplateId.ToString() == templateId);
        if (autoDeployOnly)
        {
            query = query.Where(i => i.AutoDeployOnNewBuild);
        }
        var list = await query.ToListAsync(ct).ConfigureAwait(false);
        if (list.Count == 0)
        {
            return Array.Empty<InstanceForDeployView>();
        }
        // Todos comparten TemplateId ⇒ resolvemos ProjectId una sola vez.
        var projectId = await ResolveProjectIdAsync(list[0].TemplateId, ct).ConfigureAwait(false);
        return list.Select(i => Project(i, projectId)).ToList();
    }

    public async Task<IReadOnlyList<InstanceForDeployView>> FindByClientAsync(
        string clientId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(clientId);
        var list = await db.Instances
            .AsNoTracking()
            .Include(i => i.Ports)
            .Where(i => i.ClientId.ToString() == clientId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
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
            PrimaryContainerPort: primaryPort);
    }
}
