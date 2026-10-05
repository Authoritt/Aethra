using System.Net;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Aethra.Api.Tests;

/// <summary>
/// OT-012 (H1 de OT-011). La prueba que manda: por los hosts de las apps (Yunke, Ekippo,
/// RelayCore) TODO llega a la app — incluidas sus propias /auth, /api, /hubs — y nada a Aethra.
/// Antes, las rutas de Aethra (literales) le ganaban al catch-all de YARP en cualquier host: el
/// login del panel quedaba alcanzable por <c>yunke.</c> sin Cloudflare Access.
/// </summary>
public sealed class PanelHostTests(AethraApiFactory factory) : IClassFixture<AethraApiFactory>
{
    private async Task<(HttpStatusCode Status, string Body)> SendAsync(
        HttpMethod method, string host, string path, Action<HttpRequestMessage>? tweak = null)
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var req = new HttpRequestMessage(method, path);
        req.Headers.Host = host;
        if (method == HttpMethod.Post)
        {
            req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }
        tweak?.Invoke(req);
        using var res = await client.SendAsync(req);
        return (res.StatusCode, await res.Content.ReadAsStringAsync());
    }

    public static TheoryData<string, string, string, string> AppHostRoutes => new()
    {
        // host, metodo, ruta, cluster que DEBE recibirla
        { "yunke.test", "GET", "/", "yunke-web" },
        { "yunke.test", "POST", "/auth/login", "yunke-web" },
        { "yunke.test", "POST", "/auth/login/totp", "yunke-web" },
        { "yunke.test", "GET", "/auth/me", "yunke-web" },
        { "yunke.test", "GET", "/api/identity/users", "yunke-web" },
        { "yunke.test", "GET", "/api/instances", "yunke-web" },
        { "yunke.test", "GET", "/api/vms", "yunke-web" },
        { "yunke.test", "POST", "/hubs/dashboard/negotiate", "yunke-web" },
        { "yunke.test", "POST", "/hubs/satellite/negotiate", "yunke-web" },
        { "yunke.test", "GET", "/openapi/v1.json", "yunke-web" },
        { "yunke.test", "POST", "/mcp", "yunke-web" },
        { "yunke.test", "GET", "/context", "yunke-web" },
        { "yunke.test", "POST", "/webhooks/git", "yunke-web" },
        { "yunke.test", "GET", "/install-satellite.sh", "yunke-web" },
        { "yunke.test", "GET", "/api/satellite/binary", "yunke-web" },
        { "yunke-api.test", "POST", "/api/auth/admin/login", "yunke-api" },
        { "yunke-api.test", "POST", "/hubs/client/negotiate", "yunke-api" },
        { "yunke-api.test", "GET", "/api/instances", "yunke-api" },
        { "ekippo.test", "GET", "/", "ekippo-web" },
        { "ekippo.test", "GET", "/api/identity/users", "ekippo-api" },
        { "ekippo.test", "POST", "/api/auth/login", "ekippo-api" },
        { "ekippo.test", "GET", "/auth/me", "ekippo-web" },
        { "ekippo.test", "POST", "/auth/login", "ekippo-web" },
        { "ekippo.test", "GET", "/openapi/v1.json", "ekippo-web" },
        { "relaycore.test", "GET", "/", "relaycore" },
        { "relaycore.test", "POST", "/auth/login", "relaycore" },
        { "relaycore.test", "GET", "/api/identity/users", "relaycore" },
        { "relaycore.test", "POST", "/hubs/dashboard/negotiate", "relaycore" },
    };

    [Theory]
    [MemberData(nameof(AppHostRoutes))]
    public async Task App_hosts_reach_their_app_for_every_path_including_aethra_named_ones(
        string host, string method, string path, string cluster)
    {
        var (status, body) = await SendAsync(new HttpMethod(method), host, path);

        status.Should().Be(HttpStatusCode.OK, $"{method} {host}{path} debe proxearse a la app");
        body.Should().Be($"app:{cluster} {method} {path}");
    }

    [Theory]
    [InlineData(AethraApiFactory.PanelHost)]
    [InlineData("localhost")]
    [InlineData("localhost:5080")]
    [InlineData("127.0.0.1:5080")]
    public async Task Panel_and_loopback_hosts_still_reach_aethra(string host)
    {
        var (status, body) = await SendAsync(HttpMethod.Get, host, "/auth/me");

        status.Should().Be(HttpStatusCode.Unauthorized, "es el /auth/me de Aethra, anonimo");
        body.Should().NotStartWith("app:");
    }

    [Theory]
    [InlineData("POST", "/hubs/satellite/negotiate")]
    [InlineData("POST", "/hubs/dashboard/negotiate")]
    [InlineData("POST", "/mcp")]
    [InlineData("GET", "/context")]
    [InlineData("GET", "/api/instances")]
    [InlineData("GET", "/api/identity/users")]
    [InlineData("GET", "/openapi/v1.json")]
    public async Task Machine_callers_on_the_panel_host_still_reach_aethra(string method, string path)
    {
        // Satelites (/hubs/satellite), MCP y la API entran por aethra.: siguen llegando a Aethra
        // (401 anonimo = el endpoint de Aethra contesto; el stub de la app diria 200 app:...).
        var (status, body) = await SendAsync(new HttpMethod(method), AethraApiFactory.PanelHost, path);

        status.Should().Be(HttpStatusCode.Unauthorized);
        body.Should().NotStartWith("app:");
    }

    [Fact]
    public async Task Panel_host_root_still_goes_to_the_dashboard_through_the_proxy()
    {
        var (status, body) = await SendAsync(HttpMethod.Get, AethraApiFactory.PanelHost, "/");

        status.Should().Be(HttpStatusCode.OK);
        body.Should().Be("app:aethra-web GET /");
    }

    [Fact]
    public async Task Unrouted_host_gets_404_for_aethra_routes()
    {
        var (status, _) = await SendAsync(HttpMethod.Get, "unknown.test", "/auth/me");
        status.Should().Be(HttpStatusCode.NotFound);

        var (loginStatus, _) = await SendAsync(HttpMethod.Post, "unknown.test", "/auth/login");
        loginStatus.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task X_Forwarded_Host_cannot_smuggle_an_app_host_into_the_panel()
    {
        // KnownProxies esta vacio y XForwardedHost esta activo: si el matcher usara el host
        // reescrito, bastaria esta cabecera para volver a H1.
        var (status, body) = await SendAsync(HttpMethod.Get, "yunke.test", "/auth/me",
            r => r.Headers.Add("X-Forwarded-Host", AethraApiFactory.PanelHost));

        status.Should().Be(HttpStatusCode.OK);
        body.Should().Be("app:yunke-web GET /auth/me");
    }

    [Theory]
    [InlineData("yunke.test")]
    [InlineData(AethraApiFactory.PanelHost)]
    public async Task Health_liveness_is_unchanged_on_every_host(string host)
    {
        // Decision OT-012: /health no se mueve (monitores pueden mirarlo por el host de una app).
        var (status, body) = await SendAsync(HttpMethod.Get, host, "/health");

        status.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("aethra-api");
    }

    [Fact]
    public async Task Acme_http01_challenge_is_still_answered_by_aethra_on_app_hosts()
    {
        var (status, body) = await SendAsync(HttpMethod.Get, "yunke.test", "/.well-known/acme-challenge/no-such-token");

        // 404 del endpoint de ACME de Aethra (token desconocido); si lo hubiera tomado la app,
        // el stub diria app:...
        status.Should().Be(HttpStatusCode.NotFound);
        body.Should().NotStartWith("app:");
    }

    [Theory]
    [InlineData("localhost", AethraApiFactory.OtherContainer, HttpStatusCode.NotFound)]
    [InlineData("127.0.0.1:5080", AethraApiFactory.OtherContainer, HttpStatusCode.NotFound)]
    [InlineData("localhost", AethraApiFactory.BridgeGateway, HttpStatusCode.Unauthorized)]
    [InlineData("localhost", "127.0.0.1", HttpStatusCode.Unauthorized)]
    public async Task Loopback_host_only_opens_the_panel_to_a_real_local_peer(
        string host, string peer, HttpStatusCode expected)
    {
        // G2 de OT-012: Host: localhost esquiva Cloudflare Access. Desde otro contenedor de la red
        // (no el gateway exacto) responde 404, igual que un host desconocido.
        var (status, body) = await SendAsync(HttpMethod.Get, host, "/auth/me",
            r => r.Headers.Add(AethraApiFactory.PeerHeader, peer));

        status.Should().Be(expected);
        body.Should().NotStartWith("app:");
    }
}
