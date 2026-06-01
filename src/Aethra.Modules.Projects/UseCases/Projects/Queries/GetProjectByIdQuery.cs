using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.UseCases.Projects.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Projects.Queries;

/// <summary>
/// Detalle de un <c>Project</c>. Igual que la summary pero la UI llama esto al entrar a la
/// pantalla del proyecto para mostrar metadata aislada.
/// </summary>
public sealed record GetProjectByIdQuery(string ProjectId) : IQuery<ProjectDetail>;

internal sealed class GetProjectByIdHandler(ProjectsDbContext db)
    : IQueryHandler<GetProjectByIdQuery, ProjectDetail>
{
    public async Task<Result<ProjectDetail>> Handle(
        GetProjectByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.ProjectId, out var parsed) || parsed.Value.Prefix != "prj")
        {
            return Error.Validation("project.invalid_id", "ID de proyecto inválido.");
        }
        var projectId = new ProjectId(parsed.Value);

        var row = await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
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
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return Error.NotFound("project.not_found", $"Proyecto '{request.ProjectId}' no existe.");
        }

        return new ProjectDetail(
            id: row.Id.ToString(),
            slug: row.Slug,
            name: row.Name,
            description: row.Description,
            color: row.Color,
            icon: row.Icon,
            templateCount: row.TemplateCount,
            clientCount: row.ClientCount,
            createdAt: row.CreatedAt,
            updatedAt: row.UpdatedAt);
    }
}
