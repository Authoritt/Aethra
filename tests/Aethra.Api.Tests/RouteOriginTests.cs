using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Aethra.Modules.Proxy.Presentation;
using Aethra.Modules.Proxy.UseCases.Dtos;
using Aethra.Modules.Proxy.UseCases.Routes.Commands;
using Aethra.Shared.Contracts.Authentication;
using Aethra.Shared.Kernel.Results;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Aethra.Api.Tests;

/// <summary>
/// GitHub #102 — el origen de una ruta es procedencia asignada por el servidor, no una declaracion
/// de propiedad aceptada del cliente que luego gobierna si el deploy nativo puede borrarla.
/// </summary>
public sealed class RouteOriginTests
{
    [Fact]
    public async Task Public_api_ignores_client_origin_and_assigns_manual()
    {
        CreateRouteCommand? captured = null;
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<CreateRouteCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<CreateRouteCommand>();
                return Task.FromResult(Result.Success(RouteDtoFor(captured)));
            });

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(mediator);
        builder.Services
            .AddAuthentication(TestAuthenticationHandler.AuthenticationSchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.AuthenticationSchemeName, _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy(
            "scope:proxy:write",
            policy => policy.RequireClaim(ApiKeyAuthSchemes.ScopeClaim, "proxy:write")));

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapRoutesEndpoints();
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var body = new StringContent(
            """
            {
              "hostname": "shadow.example.com",
              "backendUrl": "http://foreign-api:8080",
              "tlsEnabled": false,
              "origin": "native_deploy"
            }
            """,
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync("/api/proxy/routes/", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        captured.Should().NotBeNull();
        captured!.Origin.Should().Be("manual");
        typeof(RoutesEndpoints.CreateRouteRequest).GetProperty("Origin").Should().BeNull();
    }

    private static RouteDto RouteDtoFor(CreateRouteCommand command) => new(
        Id: "rt_test",
        Hostname: command.Hostname,
        PathPrefix: command.PathPrefix ?? "/",
        BackendUrl: command.BackendUrl,
        TlsEnabled: command.TlsEnabled,
        CertStatus: "none",
        CertExpiresAt: null,
        OperationalOwnerType: command.OperationalOwnerType,
        OperationalOwnerId: command.OperationalOwnerId,
        Origin: command.Origin,
        CreatedAt: DateTimeOffset.UnixEpoch,
        UpdatedAt: DateTimeOffset.UnixEpoch);

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string AuthenticationSchemeName = "route-origin-test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, "route-origin-test"),
                    new Claim(ApiKeyAuthSchemes.ScopeClaim, "proxy:write"),
                ],
                AuthenticationSchemeName));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, AuthenticationSchemeName)));
        }
    }
}
