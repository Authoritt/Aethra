using System.Security.Claims;
using System.Threading;

namespace Aethra.Modules.Mcp.Security;

/// <summary>
/// Acceso al <see cref="ClaimsPrincipal"/> de la sesión MCP actual. Capturado al inicio de la
/// sesión (callback <c>ConfigureSessionOptions</c> del SDK) y propagado via
/// <see cref="System.Threading.AsyncLocal{T}"/> al background loop del transport.
///
/// <para>
/// Diseño:
/// <list type="bullet">
/// <item>Singleton — comparte el mismo <see cref="System.Threading.AsyncLocal{T}"/> entre todos los
/// scopes que el SDK crea por tool call.</item>
/// <item><see cref="System.Threading.AsyncLocal{T}"/> se establece UNA vez por sesión cuando se
/// captura el <see cref="ClaimsPrincipal"/> del HttpContext inicial. El SDK arranca
/// <c>session.RunAsync</c> en una task cuyo <c>ExecutionContext</c> flow-ea el valor.</item>
/// <item>Sesiones concurrentes (de distintos usuarios o procesos) son ortogonales: cada una tiene
/// su propio <c>ExecutionContext</c> y por tanto su propio valor de la <see cref="System.Threading.AsyncLocal{T}"/>.</item>
/// </list>
/// </para>
/// </summary>
public interface IMcpSessionPrincipalAccessor
{
    /// <summary><see cref="ClaimsPrincipal"/> del caller que abrió la sesión MCP, o <see langword="null"/> si no se capturó.</summary>
    ClaimsPrincipal? CurrentPrincipal { get; }

    /// <summary>
    /// Captura el principal para la <c>ExecutionContext</c> actual. Invocar UNA vez por sesión
    /// (dentro de <c>HttpServerTransportOptions.ConfigureSessionOptions</c>) — la
    /// <see cref="System.Threading.AsyncLocal{T}"/> arrastra el valor a las tasks que el SDK
    /// arranca después.
    /// </summary>
    void Capture(ClaimsPrincipal principal);
}

internal sealed class McpSessionPrincipalAccessor : IMcpSessionPrincipalAccessor
{
    private static readonly AsyncLocal<ClaimsPrincipal?> Holder = new();

    public ClaimsPrincipal? CurrentPrincipal => Holder.Value;

    public void Capture(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        Holder.Value = principal;
    }
}
