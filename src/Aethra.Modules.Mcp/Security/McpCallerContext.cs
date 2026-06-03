using System.Security.Claims;
using Aethra.Shared.Contracts.Authentication;

namespace Aethra.Modules.Mcp.Security;

/// <summary>
/// Lee del <see cref="ClaimsPrincipal"/> capturado al inicio de la sesión MCP el ApiKey id +
/// scopes del caller (la cookie session admin también funciona — sus claims se capturan igual).
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

    /// <summary>
    /// F12.3 — userId Aethra asociado al caller (claim NameIdentifier). <c>null</c> si es
    /// cookie bootstrap admin o API key sin owner_user_id. Lo usan tools que ejecutan
    /// operaciones "del propio user" (ej. update profile).
    /// </summary>
    string? UserId { get; }
}

/// <summary>
/// Implementación que lee del <see cref="IMcpSessionPrincipalAccessor"/>. Capturamos el
/// <see cref="ClaimsPrincipal"/> al iniciar la sesión MCP (en <c>ConfigureSessionOptions</c>) y
/// lo propagamos via <see cref="System.Threading.AsyncLocal{T}"/>: cuando el SDK ejecuta los
/// handlers de tools en un Task de background (consumer del channel del transport), la
/// <c>ExecutionContext</c> capturada en la creación de la task arrastra el principal capturado.
///
/// <para>
/// Por qué NO <see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor"/>: el SDK
/// <c>ModelContextProtocol</c> Streamable HTTP corre <c>session.RunAsync</c> como una task de
/// background que consume mensajes del transport channel. Cuando llega un <c>tools/call</c>,
/// el handler corre en la <c>ExecutionContext</c> de esa task, no en la del request HTTP que
/// trajo el mensaje. El <c>HttpContextAccessor</c> usa <see cref="System.Threading.AsyncLocal{T}"/>
/// de un <c>HttpContextHolder</c> cuya referencia ASP.NET pone en <c>null</c> al terminar el
/// request — así que cualquier task que capturó esa AsyncLocal ve <c>null</c>. Resultado:
/// las claims se pierden y todas las tools devuelven <c>insufficient_scope</c>.
/// </para>
/// </summary>
internal sealed class HttpMcpCallerContext(IMcpSessionPrincipalAccessor accessor) : IMcpCallerContext
{
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>(StringComparer.Ordinal);

    private (string Id, string Source, IReadOnlySet<string> Scopes) Resolve()
    {
        var user = accessor.CurrentPrincipal;
        if (user is null || user.Identity?.IsAuthenticated != true)
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
        // Una sesión cookie (single-user admin) sin claims de scope se trata como admin de facto.
        if (scopes.Count == 0 && string.IsNullOrEmpty(id))
        {
            scopes.Add(ApiKeyAuthSchemes.AdminScope);
        }
        return (id, source, scopes);
    }

    public string ApiKeyId => Resolve().Id;
    public string AuditSource => Resolve().Source;
    public IReadOnlySet<string> Scopes => Resolve().Scopes;
    public bool HasScope(string scope)
    {
        var s = Resolve().Scopes;
        return s.Contains(ApiKeyAuthSchemes.AdminScope) || s.Contains(scope);
    }

    public string? UserId
    {
        get
        {
            var user = accessor.CurrentPrincipal;
            if (user is null || user.Identity?.IsAuthenticated != true)
            {
                return null;
            }
            var sub = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return string.IsNullOrWhiteSpace(sub) ? null : sub;
        }
    }
}
