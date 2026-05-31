using System.Security.Claims;
using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;

namespace Aethra.Modules.Mcp.Security;

/// <summary>
/// Lee del <see cref="HttpContext"/> el ApiKey id + scopes del caller (cookie session
/// admin también funciona si se le inyecta el claim <c>scope=*</c>).
/// </summary>
public interface IMcpCallerContext
{
    /// <summary>Id de la API key. <see cref="string.Empty"/> si la sesión es cookie sin api-key id.</summary>
    string ApiKeyId { get; }

    /// <summary>Source string para audit en mutaciones (<c>"mcp:apikey:{id}"</c> o <c>"mcp:cookie"</c>).</summary>
    string AuditSource { get; }

    /// <summary>Conjunto de scopes del caller. Incluye <c>"*"</c> si es admin.</summary>
    IReadOnlySet<string> Scopes { get; }

    /// <summary>true si el caller tiene <paramref name="scope"/> exacto o el wildcard <c>"*"</c>.</summary>
    bool HasScope(string scope);
}

internal sealed class HttpMcpCallerContext(IHttpContextAccessor accessor) : IMcpCallerContext
{
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>(StringComparer.Ordinal);

    private (string Id, string Source, IReadOnlySet<string> Scopes) Resolve()
    {
        var user = accessor.HttpContext?.User;
        if (user is null || !user.Identity?.IsAuthenticated == true)
        {
            return (string.Empty, "mcp:anonymous", Empty);
        }

        var id = user.FindFirst(ApiKeyAuthSchemes.ApiKeyIdClaim)?.Value ?? string.Empty;
        var source = string.IsNullOrEmpty(id) ? "mcp:cookie" : $"mcp:apikey:{id}";

        var scopes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var claim in user.FindAll(ApiKeyAuthSchemes.ScopeClaim))
        {
            scopes.Add(claim.Value);
        }
        // Una sesión cookie (single-user admin) no trae claims de scope pero es admin de facto.
        // Si el principal está autenticado por cookie y no hay claims de scope, se trata como admin.
        if (scopes.Count == 0 && string.IsNullOrEmpty(id))
        {
            scopes.Add(ApiKey.AdminScope);
        }
        return (id, source, scopes);
    }

    public string ApiKeyId => Resolve().Id;
    public string AuditSource => Resolve().Source;
    public IReadOnlySet<string> Scopes => Resolve().Scopes;
    public bool HasScope(string scope)
    {
        var s = Resolve().Scopes;
        return s.Contains(ApiKey.AdminScope) || s.Contains(scope);
    }
}
