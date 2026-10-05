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
/// que es como se opera por SSH con <c>Host: localhost</c>, y solo desde un par local real: ver
/// <see cref="UsePanelLoopbackHostGuard"/>). En cualquier otro host el endpoint de Aethra queda
/// INVALIDO para el matcher y la peticion cae al catch-all de YARP de ese host — la app la recibe
/// tal cual, como si Aethra no existiera — o a 404 si ningun proxy-route la reclama.
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
        group.WithMetadata(PanelSurfaceMarker.Instance);
        if (panelHosts is { Length: > 0 })
        {
            group.RequireHost(panelHosts);
        }
        return group;
    }

    /// <summary>
    /// G2 de OT-012 — <c>Host: localhost</c>/<c>127.0.0.1</c> abre el panel SIN Cloudflare Access,
    /// asi que solo vale si el par TCP real es local (<see cref="PanelPeers.IsLocal"/>). Sin esto,
    /// cualquier contenedor de la red Docker (una app con RCE/SSRF) entraba al panel mandando ese
    /// Host a <c>aethra-central:5080</c>. Va ANTES de <c>UseForwardedHeaders</c> (mira el Host y el
    /// par originales); el endpoint ya esta resuelto (UseRouting implicito). Responde 404, lo mismo
    /// que un host desconocido.
    /// </summary>
    public static IApplicationBuilder UsePanelLoopbackHostGuard(this IApplicationBuilder app, PanelPeers peers)
    {
        ArgumentNullException.ThrowIfNull(peers);
        return app.Use((ctx, next) =>
        {
            if (ctx.GetEndpoint()?.Metadata.GetMetadata<PanelSurfaceMarker>() is not null
                && LoopbackHosts.Contains(ctx.Request.Host.Host, StringComparer.OrdinalIgnoreCase)
                && !peers.IsLocal(ClientAddress.RawPeer(ctx)))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }
            return next(ctx);
        });
    }
}

/// <summary>Marca los endpoints de la superficie propia de Aethra (grupo del panel).</summary>
public sealed class PanelSurfaceMarker
{
    public static readonly PanelSurfaceMarker Instance = new();

    private PanelSurfaceMarker()
    {
    }
}

/// <summary>
/// G2 de OT-012 — de que pares TCP (el par REAL, capturado antes de forwarded headers) se fia el
/// panel.
/// <list type="bullet">
/// <item><b>Locales</b> (<c>Panel:LocalPeers</c>, se SUMAN a 127.0.0.0/8 y ::1): la propia
/// maquina. En produccion el central es un contenedor con el puerto publicado en 127.0.0.1, asi que
/// lo que llega desde el host (scripts por SSH, <c>aethra-report.py</c>, cloudflared) lo entrega
/// docker-proxy con la IP del GATEWAY del bridge: esa IP EXACTA, nunca toda la 172.16/12 — los
/// demas contenedores de la red (las apps) tienen otras IPs y NO son locales. Obtenerla en la VM:
/// <c>docker network inspect aethra-net -f '{{(index .IPAM.Config 0).Gateway}}'</c>.</item>
/// <item><b>Tunel</b> (<c>Panel:TunnelPeers</c>; por defecto = los locales): pares de los que se
/// acepta <c>CF-Connecting-IP</c>. cloudflared corre en el host (o en contenedor con
/// <c>network host</c>) y entra por la misma publicacion: suele ser el mismo gateway.</item>
/// </list>
/// Una entrada sin <c>/</c> es una IP exacta (/32 o /128).
/// </summary>
public sealed class PanelPeers
{
    public static readonly IReadOnlyList<IPNetwork> Loopback =
    [
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("::1/128"),
    ];

    public PanelPeers(IReadOnlyList<IPNetwork> local, IReadOnlyList<IPNetwork> tunnel)
    {
        Local = local;
        Tunnel = tunnel;
    }

    public IReadOnlyList<IPNetwork> Local { get; }

    public IReadOnlyList<IPNetwork> Tunnel { get; }

    /// <summary>True si ademas de loopback hay pares locales configurados (central en contenedor).</summary>
    public bool HasConfiguredLocalPeers => Local.Count > Loopback.Count;

    public bool IsLocal(IPAddress? peer) => peer is not null && Local.Any(n => n.Contains(Normalize(peer)));

    public bool IsTunnel(IPAddress? peer) => peer is not null && Tunnel.Any(n => n.Contains(Normalize(peer)));

    public static PanelPeers FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var local = Loopback.Concat(Parse(configuration, "Panel:LocalPeers")).ToList();
        var tunnel = Parse(configuration, "Panel:TunnelPeers").ToList();
        return new PanelPeers(local, tunnel.Count > 0 ? tunnel : local);
    }

    private static IEnumerable<IPNetwork> Parse(IConfiguration configuration, string key)
    {
        foreach (var raw in configuration.GetSection(key).Get<string[]>() ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }
            var value = raw.Trim();
            if (value.Contains('/', StringComparison.Ordinal))
            {
                yield return IPNetwork.Parse(value);
                continue;
            }
            var ip = Normalize(IPAddress.Parse(value));
            yield return new IPNetwork(ip, ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32);
        }
    }

    private static IPAddress Normalize(IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
}

/// <summary>
/// OT-012 — IP del cliente para limitar por origen sin fiarse de cabeceras arbitrarias.
///
/// <c>UseForwardedHeaders</c> esta configurado sin <c>KnownProxies</c> (ver Program.cs), asi que
/// <c>Connection.RemoteIpAddress</c> despues de el sale de <c>X-Forwarded-For</c>, que cualquiera
/// puede mandar. Por eso se captura el par TCP real ANTES de ese middleware y solo se acepta
/// <c>CF-Connecting-IP</c> (la pone Cloudflare) cuando el par es el del conector del tunel.
/// </summary>
public static class ClientAddress
{
    private const string PeerItemKey = "aethra.raw-peer-ip";
    public const string CloudflareClientIpHeader = "CF-Connecting-IP";

    /// <summary>Debe ir ANTES de <c>UseForwardedHeaders</c>.</summary>
    public static IApplicationBuilder UseRawPeerCapture(this IApplicationBuilder app)
        => app.Use((ctx, next) =>
        {
            ctx.Items[PeerItemKey] = ctx.Connection.RemoteIpAddress;
            return next(ctx);
        });

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
    /// Clave de particion del limitador del login, o <c>null</c> = sin limite:
    /// <list type="bullet">
    /// <item>par del tunel + <c>CF-Connecting-IP</c> valida: el cliente real (<c>cf:ip</c>).</item>
    /// <item>par local sin esa cabecera: automatizacion de la propia maquina (scripts de deploy,
    /// <c>aethra-report.py</c>, agentes con <c>Host: localhost</c>) — sin limite: no comparte cupo
    /// con nadie ni corta deploys (G2 de OT-012).</item>
    /// <item>cualquier otro par (p. ej. un contenedor de la red): su IP, ignorando la cabecera.</item>
    /// </list>
    /// </summary>
    public static string? ForRateLimit(HttpContext context, PanelPeers peers)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(peers);
        var peer = RawPeer(context);
        if (peers.IsTunnel(peer))
        {
            var header = context.Request.Headers[CloudflareClientIpHeader].ToString().Trim();
            if (IPAddress.TryParse(header, out var client))
            {
                return "cf:" + client;
            }
        }
        if (peers.IsLocal(peer))
        {
            return null;
        }
        return "peer:" + (peer?.ToString() ?? "unknown");
    }
}
