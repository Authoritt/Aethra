using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Aethra.Api.Bootstrap;
using Aethra.Shared.Contracts.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aethra.Api.Tests;

/// <summary>
/// OT-012 (H2 de OT-011): produccion corre en <c>Development</c> a proposito, y el OpenAPI era
/// anonimo justo ahi. Se prueba EN Development (el Program real migraria la BD en ese entorno,
/// asi que se monta el mismo <see cref="OpenApiAccess"/> sobre un host minimo) y, aparte, en el
/// Program real fuera de Development.
/// </summary>
public sealed class OpenApiAccessTests
{
    private const string ScopesHeader = "X-Test-Scopes";

    private static async Task<HttpStatusCode> GetOpenApiInDevelopmentAsync(string? scopes)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services
            .AddAuthentication(AuthSchemes.Cookie)
            .AddScheme<AuthenticationSchemeOptions, HeaderScopesHandler>(AuthSchemes.Cookie, _ => { })
            .AddScheme<AuthenticationSchemeOptions, HeaderScopesHandler>(AuthSchemes.ApiKey, _ => { });
        builder.Services.AddAuthorization(o => o.AddOpenApiAdminPolicy());
        builder.Services.AddOpenApi();
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAethraOpenApi();
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1.json");
        if (scopes is not null)
        {
            req.Headers.Add(ScopesHeader, scopes);
        }
        using var res = await client.SendAsync(req);
        return res.StatusCode;
    }

    [Fact]
    public async Task Anonymous_is_rejected_even_in_Development()
        => (await GetOpenApiInDevelopmentAsync(null)).Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Authenticated_non_admin_is_forbidden()
        => (await GetOpenApiInDevelopmentAsync("projects:read")).Should().Be(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Admin_gets_the_document()
        => (await GetOpenApiInDevelopmentAsync("*")).Should().Be(HttpStatusCode.OK);

    /// <summary>Autentica si llega la cabecera de scopes (simula cookie/api key con esos scopes).</summary>
    private sealed class HeaderScopesHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var raw = Request.Headers[ScopesHeader].ToString();
            if (string.IsNullOrEmpty(raw))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }
            var claims = raw.Split(',').Select(s => new Claim(ApiKeyAuthSchemes.ScopeClaim, s.Trim())).ToList();
            claims.Add(new Claim(ClaimTypes.Name, "test"));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}

/// <summary>El Program real (fuera de Development): anonimo -> 401 en el host del panel.</summary>
public sealed class OpenApiProgramTests(AethraApiFactory factory) : IClassFixture<AethraApiFactory>
{
    [Fact]
    public async Task Program_openapi_is_not_anonymous()
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var req = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1.json");
        req.Headers.Host = AethraApiFactory.PanelHost;
        using var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
