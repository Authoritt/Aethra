using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;

namespace Aethra.Modules.Identity.UseCases.Commands;

public sealed record UpdateUserCommand(
    string UserId,
    string? DisplayName,
    IReadOnlyList<string>? RoleSlugs) : ICommand;

internal sealed class UpdateUserHandler(
    IdentityDbContext db,
    IUserRepository users,
    IRoleRepository roles,
    IClock clock) : ICommandHandler<UpdateUserCommand>
{
    public async Task<Result> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        if (!TryParseUserId(request.UserId, out var typedId))
        {
            return Error.NotFound("user.not_found", $"User '{request.UserId}' no existe.");
        }
        var user = await users.GetByIdAsync(typedId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound("user.not_found", $"User '{request.UserId}' no existe.");
        }

        var now = clock.UtcNow;

        if (request.DisplayName is not null)
        {
            try
            {
                user.UpdateDisplayName(request.DisplayName, now);
            }
            catch (ArgumentException ex)
            {
                return Error.Validation("user.invalid_display_name", ex.Message);
            }
        }

        if (request.RoleSlugs is not null)
        {
            var requestedSlugs = request.RoleSlugs
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();

            if (requestedSlugs.Count == 0)
            {
                return Error.Validation("user.no_roles", "El usuario debe tener al menos un rol.");
            }

            var resolvedIds = new List<RoleId>(requestedSlugs.Count);
            foreach (var slug in requestedSlugs)
            {
                var role = await roles.FindBySlugAsync(slug, cancellationToken).ConfigureAwait(false);
                if (role is null)
                {
                    return Error.Validation("user.role_not_found", $"Rol '{slug}' no existe.");
                }
                resolvedIds.Add(role.Id);
            }

            user.ReplaceRoles(resolvedIds, now);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private static bool TryParseUserId(string raw, out UserId id)
    {
        id = default;
        if (!AethraId.TryParse(raw, out var parsed) || parsed.Value.Prefix != "usr")
        {
            return false;
        }
        id = new UserId(parsed.Value);
        return true;
    }
}
