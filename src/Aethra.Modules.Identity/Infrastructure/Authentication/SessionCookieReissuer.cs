using System.Security.Claims;
using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Shared.Contracts.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Aethra.Modules.Identity.Infrastructure.Authentication;

/// <summary>
/// #82 — re-emite la cookie de sesión del usuario ACTUAL con su security stamp vigente. Lo usan
/// las operaciones que rotan el stamp del propio usuario (revocar sus sesiones, activar o
/// desactivar 2FA): las demás sesiones caen en su próxima petición y la del que hizo el cambio
/// sigue viva. Sin esto, activar 2FA echaría al usuario justo cuando ve sus recovery codes.
/// </summary>
public static class SessionCookieReissuer
{
    /// <summary>Devuelve <c>false</c> si el usuario ya no existe o está inactivo (no re-emite).</summary>
    public static async Task<bool> ReissueAsync(HttpContext http, UserId userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        var users = http.RequestServices.GetRequiredService<IUserRepository>();
        var roles = http.RequestServices.GetRequiredService<IRoleRepository>();

        var user = await users.GetByIdAsync(userId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return false;
        }
        var roleList = await roles.ListByIdsAsync(user.Roles.Select(r => r.RoleId), ct).ConfigureAwait(false);
        var identity = CookieSessionValidator.BuildIdentityForUser(user, roleList);

        // Mismas propiedades que el login (/auth/login y /auth/login/totp).
        await http.SignInAsync(ApiKeyAuthSchemes.CookieScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true }).ConfigureAwait(false);
        return true;
    }
}
