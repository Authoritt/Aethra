using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Identity.UseCases.Commands;

public sealed record DeleteRoleCommand(string RoleId) : ICommand;

internal sealed class DeleteRoleHandler(
    IdentityDbContext db,
    IRoleRepository roles) : ICommandHandler<DeleteRoleCommand>
{
    public async Task<Result> Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.RoleId, out var parsed) || parsed.Value.Prefix != "rol")
        {
            return Error.NotFound("role.not_found", $"Role '{request.RoleId}' no existe.");
        }
        var typedId = new RoleId(parsed.Value);

        var role = await roles.GetByIdAsync(typedId, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return Error.NotFound("role.not_found", $"Role '{request.RoleId}' no existe.");
        }
        if (role.IsSystem)
        {
            return Error.Conflict("role.is_system", $"El rol '{role.Slug}' es del sistema y no puede borrarse.");
        }

        // Verificación: no permitir dejar users sin rol. Si está en uso, el caller debe
        // reasignar primero — preferimos un error explícito a un cascade implícito.
        var inUse = await db.UserRoles.AnyAsync(ur => ur.RoleId == typedId, cancellationToken).ConfigureAwait(false);
        if (inUse)
        {
            return Error.Conflict(
                "role.in_use",
                $"El rol '{role.Slug}' está asignado a uno o más usuarios. Reasigná esos usuarios antes de borrar el rol.");
        }

        db.Roles.Remove(role);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
