using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Modules.Identity.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Identity.UseCases.Queries;

public sealed record ListUsersQuery() : IQuery<IReadOnlyList<UserSummaryDto>>;

internal sealed class ListUsersHandler(IdentityDbContext db, IRoleRepository roles)
    : IQueryHandler<ListUsersQuery, IReadOnlyList<UserSummaryDto>>
{
    public async Task<Result<IReadOnlyList<UserSummaryDto>>> Handle(ListUsersQuery request, CancellationToken cancellationToken)
    {
        // Cargamos users con sus joins, luego enriquecemos cada role con su displayName
        // resolviendo el slug por id desde el repo de roles. Evita N+1 con un único batch.
        var users = await db.Users
            .AsNoTracking()
            .Include(u => u.Roles)
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var allRoleIds = users.SelectMany(u => u.Roles).Select(r => r.RoleId).Distinct().ToList();
        var rolesById = (await roles.ListByIdsAsync(allRoleIds, cancellationToken).ConfigureAwait(false))
            .ToDictionary(r => r.Id);

        IReadOnlyList<UserSummaryDto> dtos = [.. users.Select(u => new UserSummaryDto(
            Id: u.Id.ToString(),
            Email: u.Email,
            DisplayName: u.DisplayName,
            Roles: [.. u.Roles
                .Select(ur => rolesById.TryGetValue(ur.RoleId, out var role)
                    ? new RoleRefDto(role.Id.ToString(), role.Slug, role.DisplayName)
                    : null)
                .Where(r => r is not null)
                .Select(r => r!)],
            IsActive: u.IsActive,
            LastLoginAt: u.LastLoginAt,
            CreatedAt: u.CreatedAt,
            UpdatedAt: u.UpdatedAt))];

        return Result.Success(dtos);
    }
}
