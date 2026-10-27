using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Aethra.Api.Bootstrap;
using Xunit;

namespace Aethra.Api.Tests;

/// <summary>
/// Pruebas de integración para la política CORS de la API (#87).
/// Verifica que:
/// 1. Los orígenes localhost de desarrollo siguen funcionando sin configuración adicional.
/// 2. Orígenes arbitrarios no configurados quedan denegados.
/// 3. Orígenes configurados (LAN HTTP y dominio público HTTPS) son permitidos con credenciales y preflight.
/// 4. El uso de comodín ('*') con AllowCredentials es rechazado al arrancar con un error claro.
/// </summary>
public sealed class CorsOriginsTests(AethraApiFactory defaultFactory) : IClassFixture<AethraApiFactory>
{
    private static HttpRequestMessage CreatePreflightRequest(
        string origin,
        string host = AethraApiFactory.PanelHost,
        string path = "/auth/me")
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Host = host;
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return request;
    }

    private static HttpRequestMessage CreateActualRequest(
        string origin,
        string host = AethraApiFactory.PanelHost,
        string path = "/auth/me")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = host;
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Cookie", "aethra.sid=test-session");
        return request;
    }

    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("http://localhost:5173")]
    public async Task Localhost_de_desarrollo_permitido_sin_configurar_nada(string origin)
    {
        using var client = defaultFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Preflight
        using var preflight = CreatePreflightRequest(origin);
        using var preflightResponse = await client.SendAsync(preflight);

        preflightResponse.Headers.TryGetValues("Access-Control-Allow-Origin", out var preflightAllowOrigin).Should().BeTrue(
            $"el origen localhost '{origin}' debe permitirse en preflight");
        preflightAllowOrigin.Should().ContainSingle().Which.Should().Be(origin);
        preflightResponse.Headers.TryGetValues("Access-Control-Allow-Credentials", out var preflightAllowCredentials).Should().BeTrue();
        preflightAllowCredentials.Should().ContainSingle().Which.Should().Be("true");

        // Petición real con credenciales
        using var request = CreateActualRequest(origin);
        using var response = await client.SendAsync(request);

        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowOrigin).Should().BeTrue(
            $"el origen localhost '{origin}' debe permitirse en peticiones con credenciales");
        allowOrigin.Should().ContainSingle().Which.Should().Be(origin);
        response.Headers.TryGetValues("Access-Control-Allow-Credentials", out var allowCredentials).Should().BeTrue();
        allowCredentials.Should().ContainSingle().Which.Should().Be("true");
    }

    [Theory]
    [InlineData("http://malicious.example.com")]
    [InlineData("http://192.168.1.99:3000")]
    [InlineData("https://untrusted-domain.org")]
    public async Task Origen_no_configurado_queda_denegado(string origin)
    {
        using var client = defaultFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Preflight
        using var preflight = CreatePreflightRequest(origin);
        using var preflightResponse = await client.SendAsync(preflight);

        preflightResponse.Headers.TryGetValues("Access-Control-Allow-Origin", out _).Should().BeFalse(
            "un origen no configurado no debe recibir Access-Control-Allow-Origin en preflight");

        // Petición real
        using var request = CreateActualRequest(origin);
        using var response = await client.SendAsync(request);

        response.Headers.TryGetValues("Access-Control-Allow-Origin", out _).Should().BeFalse(
            "un origen no configurado no debe recibir Access-Control-Allow-Origin en la petición con credenciales");
    }

    [Theory]
    [InlineData("http://192.168.1.50:3000")]
    [InlineData("https://aethra.example.com")]
    public async Task Origen_configurado_permitido_para_lan_http_y_publico_https(string origin)
    {
        using var factory = new ConfiguredCorsAppFactory(
            ("Cors:AllowedOrigins:0", "http://192.168.1.50:3000"),
            ("Cors:AllowedOrigins:1", "https://aethra.example.com"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Preflight
        using var preflight = CreatePreflightRequest(origin);
        using var preflightResponse = await client.SendAsync(preflight);

        preflightResponse.Headers.TryGetValues("Access-Control-Allow-Origin", out var preflightAllowOrigin).Should().BeTrue(
            $"el origen configurado '{origin}' debe permitirse en preflight");
        preflightAllowOrigin.Should().ContainSingle().Which.Should().Be(origin);
        preflightResponse.Headers.TryGetValues("Access-Control-Allow-Credentials", out var preflightAllowCreds).Should().BeTrue();
        preflightAllowCreds.Should().ContainSingle().Which.Should().Be("true");

        // Petición real con credenciales
        using var request = CreateActualRequest(origin);
        using var response = await client.SendAsync(request);

        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowOrigin).Should().BeTrue(
            $"el origen configurado '{origin}' debe permitirse en peticiones con credenciales");
        allowOrigin.Should().ContainSingle().Which.Should().Be(origin);
        response.Headers.TryGetValues("Access-Control-Allow-Credentials", out var allowCreds).Should().BeTrue();
        allowCreds.Should().ContainSingle().Which.Should().Be("true");
    }

    [Fact]
    public void Comodin_en_origenes_cors_se_rechaza_al_arrancar()
    {
        var act = () =>
        {
            using var factory = new ConfiguredCorsAppFactory(("Cors:AllowedOrigins:0", "*"));
            using var client = factory.CreateClient();
        };

        act.Should().Throw<Exception>(
            "configurar un comodín '*' con AllowCredentials debe fallar al arrancar con un error explicativo");
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    [InlineData("https://example.*")]
    public void Validacion_rechaza_comodines(string wildcardOrigin)
    {
        var act = () => CorsBootstrap.ValidateOrigin(wildcardOrigin);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*comodín*");
    }

    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("http://example.com/api")]
    [InlineData("https://aethra.example.com/auth/login")]
    public void Validacion_rechaza_rutas_o_barras_finales(string originWithPath)
    {
        var act = () => CorsBootstrap.ValidateOrigin(originWithPath);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ruta ni barra final*");
    }

    [Theory]
    [InlineData("localhost:3000")]
    [InlineData("ftp://example.com")]
    [InlineData("ws://localhost:5080")]
    public void Validacion_rechaza_esquemas_invalidos_o_uris_relativas(string invalidSchemeOrigin)
    {
        var act = () => CorsBootstrap.ValidateOrigin(invalidSchemeOrigin);
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("https://example.com?query=1")]
    [InlineData("https://example.com#section")]
    public void Validacion_rechaza_consultas_y_fragmentos(string originWithExtra)
    {
        var act = () => CorsBootstrap.ValidateOrigin(originWithExtra);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*consulta*");
    }

    [Theory]
    [InlineData("http://localhost:3000", "http://localhost:3000")]
    [InlineData("http://192.168.1.100:3000", "http://192.168.1.100:3000")]
    [InlineData("https://aethra.example.com", "https://aethra.example.com")]
    public void Validacion_acepta_origenes_validos_y_los_normaliza(string raw, string expected)
    {
        CorsBootstrap.ValidateOrigin(raw).Should().Be(expected);
    }

    private sealed class ConfiguredCorsAppFactory(params (string Key, string? Value)[] configurations) : AethraApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>(
                    configurations.Select(c => new KeyValuePair<string, string?>(c.Key, c.Value))));
            });
        }
    }
}
