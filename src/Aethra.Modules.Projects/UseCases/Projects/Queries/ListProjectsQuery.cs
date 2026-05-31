using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Projects.Queries;

public sealed record ListProjectsQuery : IQuery<IReadOnlyList<ProjectDto>>;

internal sealed class ListProjectsHandler(ProjectsDbContext db)
    : IQueryHandler<ListProjectsQuery, IReadOnlyList<ProjectDto>>
{
    public async Task<Result<IReadOnlyList<ProjectDto>>> Handle(ListProjectsQuery request, CancellationToken ct)
    {
        var projects = await db.Projects
            .AsNoTracking()
            .Include(p => p.Environments)
                .ThenInclude(e => e.Applications)
            .OrderBy(p => p.Slug)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<ProjectDto>>(projects.Select(ProjectMapper.ToDto).ToList());
    }
}
