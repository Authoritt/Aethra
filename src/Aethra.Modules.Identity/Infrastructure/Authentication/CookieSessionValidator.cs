using System.Globalization;
using System.Security.Claims;
using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Shared.Contracts.Authentication;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Time;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace Aethra.Modules.Identity.Infrastructure.Authentication;

/// <summary>
/// Revalida la cookie de sesión contra el estado actual del usuario y sus roles en la base de datos (#82).
/// Garantiza que cambios de contraseña, desactivación de cuenta, revocación explícita o cambios en RBAC
/// invaliden o actualicen inmediatamente la sesión en todas las réplicas de la API.
/// </summary>
public sealed class CookieSessionValidator(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IOptions<IdentityOptions> options,
    IClock clock)
{
    public const string LastValidatedKey = "aethra:last_session_validation";

    public async Task ValidateAsync(CookieValidatePrincipalContext ctx)
    {
        if (ctx.Principal?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        // Si es una sesión bootstrap de #21, la gestiona la rama correspondiente en Program.cs.
        if (ctx.Principal.HasClaim(c => c.Type == ApiKeyAuthSchemes.BootstrapClaim && c.Value == "true"))
        {
            return;
        }

        var userIdClaim = ctx.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdClaim)
            || !AethraId.TryParse(userIdClaim, out var parsed)
            || parsed.Value.Prefix != "usr")
        {
            await RejectAndSignOutAsync(ctx).ConfigureAwait(false);
            return;
        }

        // Ventana de revalidación configurable (Identity:SessionValidationIntervalSeconds).
        // Si es > 0, guardamos el timestamp de última validación en AuthenticationProperties
        // y omitimos la consulta a BD dentro de la ventana.
        var intervalSeconds = options.Value.SessionValidationIntervalSeconds;
        if (intervalSeconds > 0)
        {
            if (ctx.Properties.Items.TryGetValue(LastValidatedKey, out var lastValidatedStr)
                && DateTimeOffset.TryParse(lastValidatedStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lastValidated))
            {
                var elapsed = clock.UtcNow - lastValidated;
                if (elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromSeconds(intervalSeconds))
                {
                    return;
                }
            }
        }

        var ct = ctx.HttpContext.RequestAborted;
        var userId = new UserId(parsed.Value);
        var user = await userRepository.GetByIdAsync(userId, ct).ConfigureAwait(false);

        // Usuario inexistente o inactivo -> invalidar inmediatamente.
        if (user is null || !user.IsActive)
        {
            await RejectAndSignOutAsync(ctx).ConfigureAwait(false);
            return;
        }

        // Claim de security stamp ausente (cookie vieja emitida antes de #82) o distinto del de la BD -> invalidar.
        var stampClaim = ctx.Principal.FindFirst(ApiKeyAuthSchemes.SecurityStampClaim)?.Value;
        if (string.IsNullOrEmpty(stampClaim) || !string.Equals(stampClaim, user.SecurityStamp, StringComparison.Ordinal))
        {
            await RejectAndSignOutAsync(ctx).ConfigureAwait(false);
            return;
        }

        // Cargar roles actuales de la BD para verificar cambios en RBAC (scopes de roles o roles asignados).
        var roleIds = user.Roles.Select(r => r.RoleId).ToArray();
        var roles = await roleRepository.ListByIdsAsync(roleIds, ct).ConfigureAwait(false);

        var currentRoleSlugs = ctx.Principal.FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);
        var currentScopes = ctx.Principal.FindAll(ApiKeyAuthSchemes.ScopeClaim)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);

        var freshRoleSlugs = roles.Select(r => r.Slug).ToHashSet(StringComparer.Ordinal);
        var freshScopes = roles.SelectMany(r => r.Scopes)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var rolesChanged = !currentRoleSlugs.SetEquals(freshRoleSlugs);
        var scopesChanged = !currentScopes.SetEquals(freshScopes);

        if (rolesChanged || scopesChanged)
        {
            // Reconstruir la identidad con los roles y scopes actuales y renovar la cookie.
            var newIdentity = BuildIdentityForUser(user, roles);
            ctx.ReplacePrincipal(new ClaimsPrincipal(newIdentity));
            if (intervalSeconds > 0)
            {
                ctx.Properties.Items[LastValidatedKey] = clock.UtcNow.ToString("O");
            }
            ctx.ShouldRenew = true;
        }
        else if (intervalSeconds > 0)
        {
            ctx.Properties.Items[LastValidatedKey] = clock.UtcNow.ToString("O");
            ctx.ShouldRenew = true;
        }
    }

    private static async Task RejectAndSignOutAsync(CookieValidatePrincipalContext ctx)
    {
        ctx.RejectPrincipal();
        await ctx.HttpContext.SignOutAsync(ApiKeyAuthSchemes.CookieScheme).ConfigureAwait(false);
    }

    public static ClaimsIdentity BuildIdentityForUser(User user, IReadOnlyList<Role> roleList)
    {
        var aggregatedScopes = roleList
            .SelectMany(r => r.Scopes)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName ?? user.Email),
            new(ApiKeyAuthSchemes.SecurityStampClaim, user.SecurityStamp),
        };
        foreach (var role in roleList)
        {
            claims.Add(new Claim(ClaimTypes.Role, role.Slug));
        }
        foreach (var scope in aggregatedScopes)
        {
            claims.Add(new Claim(ApiKeyAuthSchemes.ScopeClaim, scope));
        }
        return new ClaimsIdentity(claims, ApiKeyAuthSchemes.CookieScheme);
    }
}
