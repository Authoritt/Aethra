using Aethra.Modules.Identity.Domain;
using Microsoft.AspNetCore.Authorization;

namespace Aethra.Modules.Identity.Infrastructure.Authentication;

/// <summary>
/// Helpers para registrar las policies de autorización por scope. Cada scope del
/// catálogo <see cref="ApiKey.AllScopes"/> produce una policy <c>"scope:&lt;name&gt;"</c>.
///
/// Aceptación (F11.1):
/// <list type="bullet">
///   <item>Cookie con claim <c>scope</c> (de los roles del user) igual al scope o wildcard <c>*</c>.</item>
///   <item>Cookie con claim <c>role=admin</c>.</item>
///   <item>API key con claim <c>scope</c> exacto o wildcard.</item>
///   <item>Cookie autenticada vía bootstrap (SingleUserStore) — siempre admin equivalent.</item>
/// </list>
/// </summary>
public static class ApiKeyAuthorizationExtensions
{
    /// <summary>Prefijo del nombre de cada policy de scope.</summary>
    public const string ScopePolicyPrefix = "scope:";

    /// <summary>Devuelve el nombre de policy correspondiente a un scope.</summary>
    public static string PolicyName(string scope) => ScopePolicyPrefix + scope;

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
                    ScopeGrantAuthorization.HasEffectiveScope(ctx.User, scopeValue)));
        }
        return options;
    }
}
