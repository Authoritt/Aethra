using System.Security.Claims;
using Aethra.Modules.Identity.Domain;
using Aethra.Shared.Contracts.Authentication;
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
                    HasScopeClaim(ctx, scopeValue)
                    || HasAdminRoleClaim(ctx)));
        }
        return options;
    }

    /// <summary>
    /// True si el principal está AUTENTICADO y tiene un claim <c>scope</c> con el valor exacto o
    /// <c>*</c>. Esto cubre tanto API keys (el handler de auth emite scope claims) como cookies de
    /// users multi-user (el login agrega los scopes de sus roles).
    ///
    /// <para>La comprobación de autenticación no es redundante. Sin ella, este camino y
    /// <see cref="HasAdminRoleClaim"/> tendrían requisitos de confianza DISTINTOS para la misma
    /// decisión: cualquier esquema de autenticación futuro, transformación de claims, host de
    /// pruebas o middleware que dejara un claim <c>scope</c> sobre un principal no autenticado
    /// satisfaría la policy. El handler de API key de hoy solo emite scopes tras validar la clave,
    /// pero eso es una propiedad de ESE handler, no de la policy — y la policy es lo que protege el
    /// endpoint. Se exige aquí para que el invariante no dependa de quién puebla los claims.</para>
    /// </summary>
    private static bool HasScopeClaim(AuthorizationHandlerContext ctx, string scope)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }
        return ctx.User.HasClaim(c =>
            c.Type == ApiKeyAuthSchemes.ScopeClaim
            && (c.Value == scope || c.Value == ApiKey.AdminScope));
    }

    /// <summary>
    /// True si el principal tiene el role builtin "admin". Lo verificamos en paralelo a
    /// los scopes porque el bootstrap inicial emite role=admin sin scope=*, y porque
    /// queremos que asignar el role admin via UI sea suficiente.
    /// </summary>
    private static bool HasAdminRoleClaim(AuthorizationHandlerContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }
        return ctx.User.HasClaim(c =>
            c.Type == ClaimTypes.Role && string.Equals(c.Value, Role.AdminSlug, StringComparison.Ordinal));
    }
}
