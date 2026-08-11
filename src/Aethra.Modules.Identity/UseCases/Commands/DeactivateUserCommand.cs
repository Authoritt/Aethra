using Aethra.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
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
        //
        // El "contar y luego actualizar" no basta por sí solo: dos peticiones simultáneas contra los
        // dos últimos administradores pueden ver cada una al otro como activo y proceder ambas,
        // dejando la instalación con CERO admins. Por eso la comprobación y la escritura viajan en
        // una transacción SERIALIZABLE: PostgreSQL aborta una de las dos con error de
        // serialización, y la execution strategy la reintenta, momento en el que ya verá el estado
        // real y rechazará limpiamente. Se envuelve en la estrategia porque una transacción manual
        // con NpgsqlRetryingExecutionStrategy debe ejecutarse dentro de la unidad reintentable.
        var adminRole = await roles.FindBySlugAsync(Role.AdminSlug, cancellationToken).ConfigureAwait(false);

        Result outcome = Result.Success();
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database
                .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
                .ConfigureAwait(false);

            if (adminRole is not null)
            {
                // La pertenencia al rol admin se lee DENTRO de la transacción, no del agregado que
                // se cargó antes. Con la lectura fuera, este caso quedaba abierto: cargamos al
                // objetivo como no-admin, otra petición lo promueve y degrada al que era el último
                // admin, y ese `false` en caché haría saltarse el conteo y desactivar al que acaba
                // de quedar como único administrador. PostgreSQL no puede detectar una dependencia
                // sobre datos que la transacción nunca leyó.
                var targetIsAdmin = await db.Users
                    .AnyAsync(u => u.Id == typedId && u.Roles.Any(ur => ur.RoleId == adminRole.Id), cancellationToken)
                    .ConfigureAwait(false);

                if (targetIsAdmin)
                {
                    // Contamos otros admins activos vía SQL para no traer toda la tabla.
                    var otherAdminsActive = await db.Users
                        .Where(u => u.IsActive && u.Id != typedId && u.Roles.Any(ur => ur.RoleId == adminRole.Id))
                        .CountAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (!AdminInvariantRules.CanDeactivate(targetIsAdmin, otherAdminsActive))
                    {
                        outcome = Error.Conflict(
                            AdminInvariantRules.LastAdminErrorCode,
                            "No se puede desactivar al último admin activo.");
                        return;
                    }
                }
            }

            user.Deactivate(clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            outcome = Result.Success();
        }).ConfigureAwait(false);

        return outcome;
    }
}
