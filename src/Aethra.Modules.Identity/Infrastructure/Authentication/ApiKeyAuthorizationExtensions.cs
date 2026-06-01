using Aethra.Modules.Identity.Domain;
using Aethra.Shared.Contracts.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace Aethra.Modules.Identity.Infrastructure.Authentication;

/// <summary>
/// Helpers para registrar las policies de autorización por scope. Cada scope del
/// catálogo <see cref="ApiKey.AllScopes"/> produce una policy <c>"scope:&lt;name&gt;"</c>.
///
/// Una API key debe presentar un claim <c>scope</c> con el valor exacto del scope o
/// el wildcard <c>"*"</c> (admin). Una sesión cookie (single-user admin) NO tiene
/// claims de scope, pero al estar autenticada via el cookie scheme se considera admin
/// de facto y pasa todas las policies — replicando el comportamiento de
/// <c>HttpMcpCallerContext</c>.
/// </summary>
public static class ApiKeyAuthorizationExtensions
{
    /// <summary>Prefijo del nombre de cada policy de scope.</summary>
    public const string ScopePolicyPrefix = "scope:";

    /// <summary>Devuelve el nombre de policy correspondiente a un scope.</summary>
    public static string PolicyName(string scope) => ScopePolicyPrefix + scope;

    /// <summary>
    /// Registra una policy por cada scope del catálogo.
    ///
    /// Cada policy pasa si alguna de estas condiciones se cumple:
    /// <list type="bullet">
    ///   <item>El principal está autenticado via el cookie scheme (sesión admin del UI).</item>
    ///   <item>El claim <c>scope</c> contiene el valor exacto del scope.</item>
    ///   <item>El claim <c>scope</c> contiene el wildcard <c>"*"</c> (admin api-key).</item>
    /// </list>
    /// </summary>
    public static AuthorizationOptions AddApiKeyScopePolicies(this AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        foreach (var scope in ApiKey.AllScopes)
        {
            if (scope == ApiKey.AdminScope)
            {
                continue;
            }
            var policyName = PolicyName(scope);
            var scopeValue = scope;
            options.AddPolicy(policyName, policy => policy
                .RequireAssertion(ctx =>
                    IsCookieAuthenticated(ctx)
                    || ctx.User.HasClaim(c =>
                        c.Type == ApiKeyAuthSchemes.ScopeClaim
                        && (c.Value == scopeValue || c.Value == ApiKey.AdminScope))));
        }
        return options;
    }

    /// <summary>
    /// True si el principal está autenticado por el cookie scheme. Una sesión cookie es
    /// el único modo single-user admin y equivale a tener todos los scopes — no añadimos
    /// claims <c>scope=*</c> en login para mantener la sesión humana visualmente limpia
    /// (sin parecer una api-key con admin).
    /// </summary>
    private static bool IsCookieAuthenticated(AuthorizationHandlerContext ctx)
    {
        if (ctx.User.Identity is { IsAuthenticated: true } identity)
        {
            return string.Equals(
                identity.AuthenticationType,
                ApiKeyAuthSchemes.CookieScheme,
                StringComparison.Ordinal);
        }
        return false;
    }
}
