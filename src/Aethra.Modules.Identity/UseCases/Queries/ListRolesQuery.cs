using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Modules.Identity.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Modules.Identity.UseCases.Queries;

public sealed record ListRolesQuery() : IQuery<IReadOnlyList<RoleDto>>;

internal sealed class ListRolesHandler(IRoleRepository roles) : IQueryHandler<ListRolesQuery, IReadOnlyList<RoleDto>>
{
    public async Task<Result<IReadOnlyList<RoleDto>>> Handle(ListRolesQuery request, CancellationToken cancellationToken)
    {
        var items = await roles.ListAllAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<RoleDto> dtos = [.. items.Select(r => new RoleDto(
            Id: r.Id.ToString(),
            Slug: r.Slug,
            DisplayName: r.DisplayName,
            Scopes: [.. r.Scopes],
            IsSystem: r.IsSystem,
            CreatedAt: r.CreatedAt,
            UpdatedAt: r.UpdatedAt))];
        return Result.Success(dtos);
    }
}
