using System.Security.Claims;
using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;

namespace Aethra.Modules.Identity.UseCases.Commands;

/// <summary>
/// Invalida todas las sesiones activas (cookies) de un usuario rotando su security stamp (#82).
/// Aplica comprobación de dominancia sobre los scopes del usuario objetivo para que un llamante
/// no pueda revocar sesiones de cuentas con privilegios superiores a los suyos, salvo que sea
/// el propio usuario revirtiendo sus propias sesiones.
/// </summary>
public sealed record RevokeUserSessionsCommand(string UserId)
    : ICommand<RevokeUserSessionsResultDto>;

public sealed record RevokeUserSessionsResultDto(string UserId, bool Revoked);

internal sealed class RevokeUserSessionsHandler(
    IdentityDbContext db,
    IUserRepository users,
    IRoleRepository roles,
    IClock clock,
    ICurrentPrincipalAccessor currentPrincipal) : ICommandHandler<RevokeUserSessionsCommand, RevokeUserSessionsResultDto>
{
    public async Task<Result<RevokeUserSessionsResultDto>> Handle(
        RevokeUserSessionsCommand request,
        CancellationToken cancellationToken)
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

        // Comprobación de dominancia: el llamante no puede revocar sesiones de un usuario
        // cuyos scopes superan a los del llamante, salvo que sea el propio usuario.
        var callerUserId = currentPrincipal.Current?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isSelf = string.Equals(callerUserId, user.Id.ToString(), StringComparison.Ordinal);

        if (!isSelf)
        {
            var targetRoles = await roles.ListByIdsAsync(user.Roles.Select(r => r.RoleId), cancellationToken).ConfigureAwait(false);
            var targetScopes = targetRoles.SelectMany(r => r.Scopes).Distinct(StringComparer.Ordinal);
            var grantError = ScopeGrantAuthorization.Validate(currentPrincipal.Current, targetScopes);
            if (grantError is not null)
            {
                return grantError;
            }
        }

        user.RotateSecurityStamp(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new RevokeUserSessionsResultDto(user.Id.ToString(), true));
    }
}
