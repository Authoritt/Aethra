using System.Net;

namespace Aethra.Api.Bootstrap;

/// <summary>
/// OT-012 (H1 de OT-011) — superficie propia de Aethra atada al host del panel.
///
/// Este proceso es a la vez la API del panel y el reverse proxy YARP de TODAS las apps: el tunel
/// de Cloudflare manda cualquier hostname de la zona a 127.0.0.1:5080. Sin restriccion de host,
/// <c>POST https://yunke.../auth/login</c> llegaba al login del PANEL saltandose Cloudflare Access
/// (que esta atado al host <c>aethra.</c>, no a la aplicacion) y el rate limit del borde.
///
/// Con <c>Panel:Hosts</c> configurado, las rutas de Aethra solo casan en esos hosts (+ loopback,
/// que es como se opera por SSH con <c>Host: localhost</c>). En cualquier otro host el endpoint de
/// Aethra queda INVALIDO para el matcher y la peticion cae al catch-all de YARP de ese host —
/// la app la recibe tal cual, como si Aethra no existiera — o a 404 si ningun proxy-route la
/// reclama.
///
/// El host que se compara es el de la conexion, NO <c>X-Forwarded-Host</c>: el enrutado
/// implicito de <c>WebApplication</c> corre ANTES que <c>UseForwardedHeaders</c>. No anadas un
/// <c>app.UseRouting()</c> explicito despues de <c>UseForwardedHeaders</c>: el host del matcher
/// pasaria a ser falsificable por cabecera (lo cubre <c>PanelHostTests</c>).
///
/// Sin <c>Panel:Hosts</c> no se restringe nada (instalacion autohospedada en localhost, el
/// comportamiento previo); el arranque lo avisa en Warning.
/// </summary>
public static class PanelSurface
{
    /// <summary>
    /// Hosts de loopback que siempre se aceptan cuando hay restriccion (operacion interna por SSH
    /// con <c>Host: localhost</c>). Sin <c>[::1]</c>: el matcher de host de ASP.NET no sabe parsear
    /// literales IPv6 (lanza al construir el grafo de rutas, lo vio PanelHostTests).
    /// </summary>
    public static readonly IReadOnlyList<string> LoopbackHosts = ["localhost", "127.0.0.1"];

    /// <summary>
    /// Hosts en los que atienden las rutas de Aethra, o <c>null</c> si <c>Panel:Hosts</c> esta
    /// vacio (sin restriccion). El puerto no se compara: <c>localhost</c> casa con <c>localhost:5080</c>.
    /// </summary>
    public static string[]? ResolvePanelHosts(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configured = configuration.GetSection("Panel:Hosts").Get<string[]>() ?? [];
        var cleaned = configured
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h.Trim())
            .ToList();
        if (cleaned.Count == 0)
        {
            return null;
        }
        return [.. cleaned.Concat(LoopbackHosts).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Grupo raiz (sin prefijo) donde se mapea toda la superficie propia de Aethra. Con hosts,
    /// cada endpoint del grupo lleva <c>RequireHost</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapPanelGroup(this IEndpointRouteBuilder app, string[]? panelHosts)
    {
        ArgumentNullException.ThrowIfNull(app);
        var group = app.MapGroup(string.Empty);
        if (panelHosts is { Length: > 0 })
        {
            group.RequireHost(panelHosts);
        }
        return group;
    }
}

/// <summary>
/// OT-012 — IP del cliente para limitar por origen sin fiarse de cabeceras arbitrarias.
///
/// <c>UseForwardedHeaders</c> esta configurado sin <c>KnownProxies</c> (ver Program.cs), asi que
/// <c>Connection.RemoteIpAddress</c> despues de el sale de <c>X-Forwarded-For</c>, que cualquiera
/// puede mandar. Por eso se captura el par TCP real ANTES de ese middleware y solo se acepta
/// <c>CF-Connecting-IP</c> (la pone Cloudflare) cuando el par es la red del tunel.
/// </summary>
public static class ClientAddress
{
    private const string PeerItemKey = "aethra.raw-peer-ip";
    public const string CloudflareClientIpHeader = "CF-Connecting-IP";

    /// <summary>
    /// Redes desde las que llega el tunel: loopback y los puentes de Docker (cloudflared en el
    /// host entra al contenedor por el gateway de la red, 172.16/12).
    /// </summary>
    public static readonly IReadOnlyList<IPNetwork> DefaultTrustedProxyNetworks =
    [
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("::1/128"),
        IPNetwork.Parse("172.16.0.0/12"),
    ];

    /// <summary>Debe ir ANTES de <c>UseForwardedHeaders</c>.</summary>
    public static IApplicationBuilder UseRawPeerCapture(this IApplicationBuilder app)
        => app.Use((ctx, next) =>
        {
            ctx.Items[PeerItemKey] = ctx.Connection.RemoteIpAddress;
            return next(ctx);
        });

    public static IReadOnlyList<IPNetwork> ResolveTrustedNetworks(IConfiguration configuration)
    {
        var raw = configuration.GetSection("Panel:TrustedProxyNetworks").Get<string[]>();
        if (raw is null || raw.Length == 0)
        {
            return DefaultTrustedProxyNetworks;
        }
        return [.. raw.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => IPNetwork.Parse(s.Trim()))];
    }

    /// <summary>Par TCP real (antes de forwarded headers); sin captura, el de la conexion.</summary>
    public static IPAddress? RawPeer(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var ip = context.Items.TryGetValue(PeerItemKey, out var v) && v is IPAddress captured
            ? captured
            : context.Connection.RemoteIpAddress;
        return ip is { IsIPv4MappedToIPv6: true } ? ip.MapToIPv4() : ip;
    }

    /// <summary>
    /// Clave de particion: <c>CF-Connecting-IP</c> si el par es de confianza y la cabecera es una
    /// IP valida; si no, el par TCP. Un cliente que llegue de otra red no elige su cubo.
    /// </summary>
    public static string ForRateLimit(HttpContext context, IReadOnlyList<IPNetwork> trustedProxies)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(trustedProxies);
        var peer = RawPeer(context);
        if (peer is not null && trustedProxies.Any(n => n.Contains(peer)))
        {
            var header = context.Request.Headers[CloudflareClientIpHeader].ToString().Trim();
            if (IPAddress.TryParse(header, out var client))
            {
                return client.ToString();
            }
        }
        return peer?.ToString() ?? "unknown";
    }
}
