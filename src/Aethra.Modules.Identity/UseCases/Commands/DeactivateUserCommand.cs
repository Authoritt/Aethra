using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;

namespace Aethra.Modules.Identity.UseCases.Commands;

/// <summary>
/// Soft-delete del user (IsActive=false). El login rechaza users inactivos,
/// pero las referencias históricas (notes, deployments) se preservan.
///
/// El handler rechaza desactivar el último admin activo para evitar lock-out
/// total del sistema — un workspace siempre debe tener al menos un admin que
/// pueda crear nuevos users.
/// </summary>
public sealed record DeactivateUserCommand(string UserId) : ICommand;

internal sealed class DeactivateUserHandler(
    IdentityDbContext db,
    IUserRepository users,
    IRoleRepository roles,
    IClock clock) : ICommandHandler<DeactivateUserCommand>
{
    public async Task<Result> Handle(DeactivateUserCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.UserId, out var parsed) || parsed.Value.Prefix != "usr")
        {
            return Error.NotFound("user.not_found", $"User '{request.UserId}' no existe.");
        }
        var typedId = new UserId(parsed.Value);

        var user = await users.GetByIdAsync(typedId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound("user.not_found", $"User '{request.UserId}' no existe.");
        }
        if (!user.IsActive)
        {
            return Result.Success();
        }

        // Guard: si es admin, asegurarse de que quede otro admin activo.
        var adminRole = await roles.FindBySlugAsync(Role.AdminSlug, cancellationToken).ConfigureAwait(false);
        if (adminRole is not null && user.Roles.Any(r => r.RoleId == adminRole.Id))
        {
            // Contamos otros admins activos vía SQL para no traer toda la tabla.
            var otherAdminsActive = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .CountAsync(
                    db.Users.Where(u => u.IsActive
                        && u.Id != typedId
                        && u.Roles.Any(ur => ur.RoleId == adminRole.Id)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (otherAdminsActive == 0)
            {
                return Error.Conflict("user.last_admin", "No se puede desactivar al último admin activo.");
            }
        }

        user.Deactivate(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
