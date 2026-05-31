using Aethra.Modules.Identity.Domain;
using Microsoft.AspNetCore.Authorization;

namespace Aethra.Modules.Identity.Infrastructure.Authentication;

/// <summary>
/// Helpers para registrar las policies de autorización por scope. Cada scope del
/// catálogo <see cref="ApiKey.AllScopes"/> produce una policy <c>"scope:&lt;name&gt;"</c>
/// que acepta el claim "scope" con valor exacto o con el wildcard <c>"*"</c>.
///
/// Una sesión por cookie también puede pasar estas policies si en login se le
/// inyecta el claim <c>scope=*</c> (admin).
/// </summary>
public static class ApiKeyAuthorizationExtensions
{
    /// <summary>Prefijo del nombre de cada policy de scope.</summary>
    public const string ScopePolicyPrefix = "scope:";

    /// <summary>Devuelve el nombre de policy correspondiente a un scope.</summary>
    public static string PolicyName(string scope) => ScopePolicyPrefix + scope;

    /// <summary>
    /// Registra una policy por cada scope del catálogo. Cada policy requiere el claim
    /// <c>scope</c> con el valor exacto del scope o con el wildcard de admin.
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
                .RequireAssertion(ctx => ctx.User.HasClaim(c =>
                    c.Type == ApiKeyAuthSchemes.ScopeClaim
                    && (c.Value == scopeValue || c.Value == ApiKey.AdminScope))));
        }
        return options;
    }
}
