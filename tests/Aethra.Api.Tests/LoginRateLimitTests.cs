using System.Net;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Aethra.Api.Tests;

/// <summary>
/// OT-012 — limitador propio del login (cupo de test: 3 por ventana). Se usa
/// <c>/auth/login/totp</c> con un challenge invalido: responde 401 sin tocar la BD, y comparte
/// cupo con <c>/auth/login</c> (misma politica, misma particion).
/// </summary>
public sealed class LoginRateLimitTests(AethraApiFactory factory) : IClassFixture<AethraApiFactory>
{
    private const string TunnelPeer = "172.18.0.1";     // gateway de Docker: por donde entra cloudflared
    private const string ForeignPeer = "198.51.100.7";  // cualquiera que no es la red del tunel

    private async Task<HttpStatusCode> TotpAsync(string peer, string? cfConnectingIp, string host = AethraApiFactory.PanelHost)
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var req = new HttpRequestMessage(HttpMethod.Post, "/auth/login/totp")
        {
            Content = new StringContent("{\"totp_token\":\"x\",\"code\":\"000000\"}", Encoding.UTF8, "application/json"),
        };
        req.Headers.Host = host;
        req.Headers.Add(AethraApiFactory.PeerHeader, peer);
        if (cfConnectingIp is not null)
        {
            req.Headers.Add("CF-Connecting-IP", cfConnectingIp);
        }
        using var res = await client.SendAsync(req);
        return res.StatusCode;
    }

    [Fact]
    public async Task Fourth_attempt_from_the_same_real_ip_is_429()
    {
        for (var i = 0; i < 3; i++)
        {
            (await TotpAsync(TunnelPeer, "203.0.113.10")).Should().Be(HttpStatusCode.Unauthorized);
        }

        (await TotpAsync(TunnelPeer, "203.0.113.10")).Should().Be((HttpStatusCode)429);
    }

    [Fact]
    public async Task Different_real_ips_behind_the_tunnel_have_separate_budgets()
    {
        for (var i = 0; i < 3; i++)
        {
            await TotpAsync(TunnelPeer, "203.0.113.20");
        }
        (await TotpAsync(TunnelPeer, "203.0.113.20")).Should().Be((HttpStatusCode)429);

        (await TotpAsync(TunnelPeer, "203.0.113.21")).Should().Be(HttpStatusCode.Unauthorized,
            "otro cliente real no hereda el bloqueo del primero");
    }

    [Fact]
    public async Task CF_Connecting_IP_from_outside_the_tunnel_network_is_ignored()
    {
        // Un cliente que no viene del tunel no puede rotar la cabecera para estrenar cupo.
        for (var i = 0; i < 3; i++)
        {
            await TotpAsync(ForeignPeer, $"203.0.113.{30 + i}");
        }

        (await TotpAsync(ForeignPeer, "203.0.113.99")).Should().Be((HttpStatusCode)429);
    }

    [Fact]
    public async Task X_Forwarded_For_cannot_pick_the_bucket_either()
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/auth/login/totp")
            {
                Content = new StringContent("{\"totp_token\":\"x\",\"code\":\"1\"}", Encoding.UTF8, "application/json"),
            };
            req.Headers.Host = AethraApiFactory.PanelHost;
            req.Headers.Add(AethraApiFactory.PeerHeader, "198.51.100.50");
            req.Headers.Add("X-Forwarded-For", $"192.0.2.{i + 1}");
            using var res = await client.SendAsync(req);
            statuses.Add(res.StatusCode);
        }

        statuses[^1].Should().Be((HttpStatusCode)429);
    }

    [Fact]
    public async Task The_app_behind_the_proxy_has_no_limiter()
    {
        // POST /auth/login/totp por el host de una app = la app; 10 seguidos, ningun 429.
        for (var i = 0; i < 10; i++)
        {
            (await TotpAsync(TunnelPeer, "203.0.113.40", host: "yunke.test")).Should().Be(HttpStatusCode.OK);
        }
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData(AethraApiFactory.BridgeGateway)]
    public async Task Local_automation_without_CF_header_is_not_limited(string localPeer)
    {
        // G2 de OT-012: scripts de deploy, aethra-report.py y agentes con Host: localhost llegan
        // sin CF-Connecting-IP por loopback/docker-proxy. Antes compartian UN cupo de 10/300 s.
        for (var i = 0; i < 10; i++)
        {
            (await TotpAsync(localPeer, null, host: "localhost")).Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task Another_container_on_the_bridge_cannot_pick_its_bucket_with_CF_header()
    {
        // Un contenedor de la red (app con RCE/SSRF) NO es el conector del tunel: su cabecera no vale.
        for (var i = 0; i < 3; i++)
        {
            await TotpAsync(AethraApiFactory.OtherContainer, $"203.0.113.{60 + i}");
        }

        (await TotpAsync(AethraApiFactory.OtherContainer, "203.0.113.99")).Should().Be((HttpStatusCode)429);
    }
}
