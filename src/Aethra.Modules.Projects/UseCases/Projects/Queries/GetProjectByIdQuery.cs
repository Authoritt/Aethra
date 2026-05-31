using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Projects.Queries;

public sealed record GetProjectByIdQuery(string ProjectId) : IQuery<ProjectDto>;

internal sealed class GetProjectByIdHandler(ProjectsDbContext db)
    : IQueryHandler<GetProjectByIdQuery, ProjectDto>
{
    public async Task<Result<ProjectDto>> Handle(GetProjectByIdQuery request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.ProjectId, out var parsed) || parsed.Value.Prefix != "prj")
        {
            return Error.Validation("project.invalid_id", "ID de proyecto inválido.");
        }
        var typedId = new ProjectId(parsed.Value);

        var project = await db.Projects
            .AsNoTracking()
            .Include(p => p.Environments)
                .ThenInclude(e => e.Applications)
            .FirstOrDefaultAsync(p => p.Id == typedId, ct)
            .ConfigureAwait(false);

        if (project is null)
        {
            return Error.NotFound("project.not_found", $"No existe el proyecto '{request.ProjectId}'.");
        }
        return ProjectMapper.ToDto(project);
    }
}
