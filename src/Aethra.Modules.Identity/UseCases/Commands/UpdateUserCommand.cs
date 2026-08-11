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

public sealed record UpdateUserCommand(
    string UserId,
    string? DisplayName,
    IReadOnlyList<string>? RoleSlugs,
    string? GitHubUsername = null,
    bool ClearGitHubUsername = false) : ICommand;

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

        if (request.ClearGitHubUsername)
        {
            try
            {
                user.SetGitHubUsername(null, now);
            }
            catch (ArgumentException ex)
            {
                return Error.Validation("user.invalid_github_username", ex.Message);
            }
        }
        else if (request.GitHubUsername is not null)
        {
            try
            {
                user.SetGitHubUsername(request.GitHubUsername, now);
            }
            catch (ArgumentException ex)
            {
                return Error.Validation("user.invalid_github_username", ex.Message);
            }
        }

        // Resolución del catálogo de roles: son lecturas de datos que no participan del invariante
        // (un rol no aparece ni desaparece por efecto de otra petición concurrente), así que se
        // hacen fuera de la transacción para no alargarla.
        List<RoleId>? resolvedIds = null;
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

            resolvedIds = new List<RoleId>(requestedSlugs.Count);
            foreach (var slug in requestedSlugs)
            {
                var role = await roles.FindBySlugAsync(slug, cancellationToken).ConfigureAwait(false);
                if (role is null)
                {
                    return Error.Validation("user.role_not_found", $"Rol '{slug}' no existe.");
                }
                resolvedIds.Add(role.Id);
            }
        }

        var adminRole = resolvedIds is null
            ? null
            : await roles.FindBySlugAsync(Role.AdminSlug, cancellationToken).ConfigureAwait(false);

        // El invariante del último administrador se aplicaba SOLO al desactivar, no al reemplazar
        // roles, así que una edición corriente podía dejar la instalación sin admins. Y no basta con
        // comprobarlo: la comprobación y la escritura tienen que ser la MISMA unidad serializable.
        // Si no, dos degradaciones simultáneas de los dos últimos admins —o una degradación contra
        // una desactivación— pueden leer cada una al otro como activo y aplicarse ambas. Es el mismo
        // fallo que el invariante persigue, un nivel más abajo.
        Result outcome = Result.Success();
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database
                .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
                .ConfigureAwait(false);

            if (resolvedIds is not null && adminRole is not null)
            {
                // Estado leído DENTRO de la transacción, no del agregado que se cargó antes: si otra
                // petición promovió o degradó a alguien mientras tanto, aquella foto ya es vieja y
                // PostgreSQL no puede detectar una dependencia que nunca vio.
                var current = await db.Users
                    .Where(u => u.Id == typedId)
                    .Select(u => new
                    {
                        u.IsActive,
                        IsAdmin = u.Roles.Any(ur => ur.RoleId == adminRole.Id),
                    })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (current is not null)
                {
                    var keepsAdmin = resolvedIds.Contains(adminRole.Id);
                    var otherActiveAdmins = await db.Users
                        .Where(u => u.IsActive && u.Id != typedId && u.Roles.Any(ur => ur.RoleId == adminRole.Id))
                        .CountAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (!AdminInvariantRules.CanReplaceRoles(
                            current.IsActive, current.IsAdmin, keepsAdmin, otherActiveAdmins))
                    {
                        outcome = Error.Conflict(
                            AdminInvariantRules.LastAdminErrorCode,
                            "No se puede quitar el rol admin al último administrador activo.");
                        return;
                    }
                }
            }

            if (resolvedIds is not null)
            {
                user.ReplaceRoles(resolvedIds, now);
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            outcome = Result.Success();
        }).ConfigureAwait(false);

        return outcome;
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
