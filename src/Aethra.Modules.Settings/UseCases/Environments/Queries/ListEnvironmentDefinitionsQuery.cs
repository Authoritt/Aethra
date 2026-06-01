using Aethra.Modules.Settings.Infrastructure;
using Aethra.Modules.Settings.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Settings.UseCases.Environments.Queries;

public sealed record ListEnvironmentDefinitionsQuery : IQuery<IReadOnlyList<EnvironmentDefinitionDto>>;

internal sealed class ListEnvironmentDefinitionsHandler(SettingsDbContext db)
    : IQueryHandler<ListEnvironmentDefinitionsQuery, IReadOnlyList<EnvironmentDefinitionDto>>
{
    public async Task<Result<IReadOnlyList<EnvironmentDefinitionDto>>> Handle(
        ListEnvironmentDefinitionsQuery request,
        CancellationToken cancellationToken)
    {
        var rows = await db.EnvironmentDefinitions
            .AsNoTracking()
            .OrderBy(e => e.Order)
            .ThenBy(e => e.Slug)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<EnvironmentDefinitionDto> dtos = [.. rows.Select(Mappers.ToDto)];
        return Result.Success(dtos);
    }
}
