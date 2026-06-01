using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.UseCases.Templates.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Templates.Queries;

/// <summary>
/// Lista los <c>Template</c>s de un <c>Project</c>. Ordenados por <c>Name</c> para que la UI no
/// tenga que reordenar en cliente.
/// </summary>
public sealed record ListTemplatesQuery(string ProjectId) : IQuery<IReadOnlyList<TemplateSummary>>;

internal sealed class ListTemplatesHandler(ProjectsDbContext db)
    : IQueryHandler<ListTemplatesQuery, IReadOnlyList<TemplateSummary>>
{
    public async Task<Result<IReadOnlyList<TemplateSummary>>> Handle(
        ListTemplatesQuery request,
        CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.ProjectId, out var parsed) || parsed.Value.Prefix != "prj")
        {
            return Error.Validation("template.invalid_project_id", "ID de proyecto inválido.");
        }
        var projectId = new ProjectId(parsed.Value);

        var rows = await db.Templates
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<TemplateSummary> dtos = [.. rows.Select(t => new TemplateSummary(
            id: t.Id.ToString(),
            projectId: t.ProjectId.ToString(),
            slug: t.Slug.Value,
            name: t.Name,
            description: t.Description,
            gitRepoUrl: t.Source.GitRepoUrl.Value,
            branch: t.Source.Branch,
            buildType: t.Build.BuildType.ToString(),
            createdAt: t.CreatedAt,
            updatedAt: t.UpdatedAt))];

        return Result.Success(dtos);
    }
}
