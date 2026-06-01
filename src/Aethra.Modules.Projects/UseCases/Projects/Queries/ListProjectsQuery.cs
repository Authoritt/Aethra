using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.UseCases.Projects.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Projects.Queries;

/// <summary>
/// Lista todos los <c>Project</c>s con contadores de Templates y Clients agregados in-query.
/// La UI consume este endpoint para el dashboard principal — no hay paginación porque el orden
/// de magnitud esperado es decenas, no miles.
/// </summary>
public sealed record ListProjectsQuery : IQuery<IReadOnlyList<ProjectSummary>>;

internal sealed class ListProjectsHandler(ProjectsDbContext db)
    : IQueryHandler<ListProjectsQuery, IReadOnlyList<ProjectSummary>>
{
    public async Task<Result<IReadOnlyList<ProjectSummary>>> Handle(
        ListProjectsQuery request,
        CancellationToken cancellationToken)
    {
        // Subquery counters: cada Project se enriquece con su número de Templates/Clients en
        // una sola roundtrip. EF traduce esto a LATERAL JOINs o subselects según provider.
        var rows = await db.Projects
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new
            {
                p.Id,
                Slug = p.Slug.Value,
                p.Name,
                p.Description,
                p.Color,
                p.Icon,
                p.CreatedAt,
                p.UpdatedAt,
                TemplateCount = db.Templates.Count(t => t.ProjectId == p.Id),
                ClientCount = db.Clients.Count(c => c.ProjectId == p.Id),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<ProjectSummary> dtos = [.. rows.Select(r => new ProjectSummary(
            id: r.Id.ToString(),
            slug: r.Slug,
            name: r.Name,
            description: r.Description,
            color: r.Color,
            icon: r.Icon,
            templateCount: r.TemplateCount,
            clientCount: r.ClientCount,
            createdAt: r.CreatedAt,
            updatedAt: r.UpdatedAt))];

        return Result.Success(dtos);
    }
}
