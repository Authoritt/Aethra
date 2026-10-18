using System.Security.Claims;
using Aethra.Modules.Identity.Domain;
using Aethra.Shared.Contracts.Authentication;
using Aethra.Shared.Kernel.Errors;

namespace Aethra.Modules.Identity.Infrastructure.Authentication;

/// <summary>
/// Aplica dominancia al conceder scopes: el llamante solo puede conceder scopes que ya posee.
/// Usa los mismos claims efectivos que las policies para que cookie y API key sigan una sola regla.
/// </summary>
internal static class ScopeGrantAuthorization
{
    internal const string ForbiddenErrorCode = "authorization.scope_not_grantable";

    internal static Error? Validate(ClaimsPrincipal caller, IEnumerable<string> grantedScopes)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(grantedScopes);

        if (caller.Identity?.IsAuthenticated != true)
        {
            return Forbidden();
        }

        return grantedScopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .All(scope => scope == ApiKey.AdminScope
                ? HasScopeClaim(caller, ApiKey.AdminScope)
                : HasEffectiveScope(caller, scope))
                ? null
                : Forbidden();
    }

    /// <summary>
    /// Misma resolución que usan las policies: identidad autenticada y claim exacto, wildcard
    /// o role admin. Cubre por igual los claims emitidos por cookie y API key.
    /// </summary>
    internal static bool HasEffectiveScope(ClaimsPrincipal caller, string scope)
    {
        if (caller.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return HasScopeClaim(caller, scope)
            || caller.HasClaim(c =>
                c.Type == ClaimTypes.Role
                && string.Equals(c.Value, Role.AdminSlug, StringComparison.Ordinal));
    }

    private static bool HasScopeClaim(ClaimsPrincipal caller, string scope)
        => caller.HasClaim(c =>
            c.Type == ApiKeyAuthSchemes.ScopeClaim
            && (c.Value == scope || c.Value == ApiKey.AdminScope));

    private static Error Forbidden() => Error.Forbidden(
        ForbiddenErrorCode,
        "No puede conceder scopes que el llamante no posee.");
}
