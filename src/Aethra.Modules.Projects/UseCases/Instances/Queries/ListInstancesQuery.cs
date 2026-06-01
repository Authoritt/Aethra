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
public sealed record ListInstancesQuery(string TemplateId) : IQuery<IReadOnlyList<InstanceSummary>>;

internal sealed class ListInstancesHandler(ProjectsDbContext db)
    : IQueryHandler<ListInstancesQuery, IReadOnlyList<InstanceSummary>>
{
    public async Task<Result<IReadOnlyList<InstanceSummary>>> Handle(
        ListInstancesQuery request,
        CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.TemplateId, out var parsed) || parsed.Value.Prefix != "tpl")
        {
            return Error.Validation("instance.invalid_template_id", "ID de template inválido.");
        }
        var templateId = new TemplateId(parsed.Value);

        var rows = await db.Instances
            .AsNoTracking()
            .Include(i => i.Ports)
            .Where(i => i.TemplateId == templateId)
            .OrderBy(i => i.Environment)
            .ThenBy(i => i.Slug)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return Result.Success<IReadOnlyList<InstanceSummary>>(Array.Empty<InstanceSummary>());
        }

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
                updatedAt: i.UpdatedAt);
        })];

        return Result.Success(dtos);
    }
}
