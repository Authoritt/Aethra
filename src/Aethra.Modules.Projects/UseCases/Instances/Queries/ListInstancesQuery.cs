using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Domain.Clients;
using Aethra.Modules.Projects.Domain.Templates;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.UseCases.Instances.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Instances.Queries;

/// <summary>
/// Lista las <c>Instance</c>s de un <c>Template</c>, ordenadas por environment + slug.
/// Resuelve el slug del client en una sola query (lookup batch) para evitar N+1 en la UI.
/// </summary>
public sealed record ListInstancesQuery(
    string? TemplateId = null,
    string? ProjectId = null,
    string? OwnerUserId = null,
    bool? IsEphemeral = null,
    string? ClientId = null) : IQuery<IReadOnlyList<InstanceSummary>>;

internal sealed class ListInstancesHandler(ProjectsDbContext db)
    : IQueryHandler<ListInstancesQuery, IReadOnlyList<InstanceSummary>>
{
    public async Task<Result<IReadOnlyList<InstanceSummary>>> Handle(
        ListInstancesQuery request,
        CancellationToken cancellationToken)
    {
        TemplateId? typedTemplateId = null;
        if (request.TemplateId is not null)
        {
            if (!AethraId.TryParse(request.TemplateId, out var parsed) || parsed.Value.Prefix != "tpl")
            {
                return Error.Validation("instance.invalid_template_id", "ID de template inválido.");
            }
            typedTemplateId = new TemplateId(parsed.Value);
        }

        ProjectId? typedProjectId = null;
        if (request.ProjectId is not null)
        {
            if (!AethraId.TryParse(request.ProjectId, out var parsed) || parsed.Value.Prefix != "prj")
            {
                return Error.Validation("instance.invalid_project_id", "ID de proyecto inválido.");
            }
            typedProjectId = new ProjectId(parsed.Value);
        }

        // Si filtramos por Project, primero resolvemos los Template ids del Project.
        List<TemplateId>? projectTemplates = null;
        if (typedProjectId is not null)
        {
            projectTemplates = await db.Templates
                .AsNoTracking()
                .Where(t => t.ProjectId == typedProjectId.Value)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var query = db.Instances.AsNoTracking().Include(i => i.Ports).AsQueryable();
        if (typedTemplateId is not null)
        {
            query = query.Where(i => i.TemplateId == typedTemplateId.Value);
        }
        if (projectTemplates is not null)
        {
            var localTemplates = projectTemplates;
            query = query.Where(i => localTemplates.Contains(i.TemplateId));
        }
        if (request.IsEphemeral is not null)
        {
            var ephemeralFlag = request.IsEphemeral.Value;
            query = query.Where(i => i.IsEphemeral == ephemeralFlag);
        }
        if (!string.IsNullOrWhiteSpace(request.OwnerUserId))
        {
            var owner = request.OwnerUserId;
            query = query.Where(i => i.CreatedByUserId == owner);
        }
        if (!string.IsNullOrWhiteSpace(request.ClientId))
        {
            if (!AethraId.TryParse(request.ClientId, out var parsedClient) || parsedClient.Value.Prefix != "cli")
            {
                return Error.Validation("instance.invalid_client_id", "ID de cliente inválido.");
            }
            var typedClientId = new ClientId(parsedClient.Value);
            query = query.Where(i => i.ClientId == typedClientId);
        }

        var rows = await query
            .OrderBy(i => i.Environment)
            .ThenBy(i => i.Slug)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return Result.Success<IReadOnlyList<InstanceSummary>>(Array.Empty<InstanceSummary>());
        }

        // F12.3 — cargar Templates para resolver el EffectiveTrackedRef.
        var templateIdsForLookup = rows.Select(r => r.TemplateId).Distinct().ToList();
        var templatesForResolve = await db.Templates
            .AsNoTracking()
            .Include(t => t.EnvironmentMapping)
            .Where(t => templateIdsForLookup.Contains(t.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var templateMap = templatesForResolve.ToDictionary(t => t.Id);

        // Resolver slugs de Client en una query batch.
        var clientIds = rows.Select(r => r.ClientId).Distinct().ToList();
        var clientSlugs = await db.Clients
            .AsNoTracking()
            .Where(c => clientIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Slug })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var slugMap = clientSlugs.ToDictionary(x => x.Id, x => x.Slug);

        IReadOnlyList<InstanceSummary> dtos = [.. rows.Select(i =>
        {
            int? primaryPort = i.Ports.Count > 0 ? i.Ports[0].ContainerPort.Value : null;
            var clientSlug = slugMap.TryGetValue(i.ClientId, out var cs) ? cs : string.Empty;
            var effective = templateMap.TryGetValue(i.TemplateId, out var tpl)
                ? i.ResolveTrackedRef(tpl)
                : null;
            return new InstanceSummary(
                id: i.Id.ToString(),
                templateId: i.TemplateId.ToString(),
                clientId: i.ClientId.ToString(),
                clientSlug: clientSlug,
                environment: i.Environment,
                slug: i.Slug,
                targetVmId: i.TargetVmId,
                containerName: i.ContainerName,
                autoDeployOnNewBuild: i.AutoDeployOnNewBuild,
                customDomain: i.CustomDomain,
                autoHostname: i.AutoHostname,
                primaryPort: primaryPort,
                createdAt: i.CreatedAt,
                updatedAt: i.UpdatedAt,
                trackedRef: i.TrackedRef,
                effectiveTrackedRef: effective,
                isEphemeral: i.IsEphemeral,
                createdByUserId: i.CreatedByUserId);
        })];

        return Result.Success(dtos);
    }
}
