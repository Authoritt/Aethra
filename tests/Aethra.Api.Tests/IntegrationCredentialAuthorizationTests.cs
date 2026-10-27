using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text;
using Aethra.Api.Bootstrap;
using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Settings.Domain;
using Aethra.Modules.Settings.UseCases.Dtos;
using Aethra.Modules.Settings.UseCases.IntegrationCredentials.Commands;
using Aethra.Modules.Settings.UseCases.IntegrationCredentials.Queries;
using Aethra.Shared.Contracts.Authentication;
using Aethra.Shared.Kernel.Results;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Aethra.Api.Tests;

public sealed class IntegrationCredentialAuthorizationTests(
    IntegrationCredentialAuthorizationFactory factory)
    : IClassFixture<IntegrationCredentialAuthorizationFactory>
{
    private const string TestCredentialId = "int_01JTESTCREDENTIAL0000000000";
    private const string CreateSecret = "create-secret-must-not-be-logged";

    public static TheoryData<string, string, string, string?, HttpStatusCode> EndpointCallerMatrix()
    {
        var endpoints = new (string Method, string Path, string? Body, HttpStatusCode AdminStatus)[]
        {
            ("GET", "/api/settings/integrations/", null, HttpStatusCode.OK),
            ("POST", "/api/settings/integrations/",
                $$"""{"name":"github:test","type":1,"displayName":"GitHub test","plainValue":"{{CreateSecret}}","metadata":null,"description":null}""",
                HttpStatusCode.Created),
            ("DELETE", $"/api/settings/integrations/{TestCredentialId}", null, HttpStatusCode.NoContent),
            ("POST", $"/api/settings/integrations/{TestCredentialId}/rotate",
                """{"newPlainValue":"rotated-secret-must-not-be-logged"}""",
                HttpStatusCode.NoContent),
        };

        var data = new TheoryData<string, string, string, string?, HttpStatusCode>();
        foreach (var endpoint in endpoints)
        {
            data.Add("ordinary", endpoint.Method, endpoint.Path, endpoint.Body, HttpStatusCode.Forbidden);
            data.Add("admin", endpoint.Method, endpoint.Path, endpoint.Body, endpoint.AdminStatus);
            data.Add("api-key", endpoint.Method, endpoint.Path, endpoint.Body, HttpStatusCode.Unauthorized);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(EndpointCallerMatrix))]
    public async Task Every_integration_endpoint_enforces_cookie_admin(
        string caller,
        string method,
        string path,
        string? body,
        HttpStatusCode expected)
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Host = AethraApiFactory.PanelHost;
        request.Headers.Add(IntegrationCredentialAuthorizationFactory.CallerHeader, caller);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(expected, $"{caller} called {method} {path}");
    }

    [Fact]
    public async Task Successful_mutation_audit_contains_actor_and_credential_id_but_not_secret()
    {
        factory.AuditLog.Clear();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/settings/integrations/")
        {
            Content = new StringContent(
                $$"""{"name":"github:test","type":1,"displayName":"GitHub test","plainValue":"{{CreateSecret}}","metadata":null,"description":null}""",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Host = AethraApiFactory.PanelHost;
        request.Headers.Add(IntegrationCredentialAuthorizationFactory.CallerHeader, "admin");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        factory.AuditLog.Entries.Should().ContainSingle();
        var entry = factory.AuditLog.Entries.Single();
        entry.Should().Contain("created").And.Contain("usr_admin").And.Contain(TestCredentialId);
        entry.Should().NotContain(CreateSecret);
    }
}

public sealed class IntegrationCredentialAuthorizationFactory : AethraApiFactory
{
    public const string CallerHeader = "X-Test-Integration-Caller";

    public CaptureAuditLoggerProvider AuditLog { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILoggerFactory>();
            services.AddSingleton<ILoggerFactory>(
                _ => LoggerFactory.Create(logging => logging.AddProvider(AuditLog)));

            services.RemoveAll<IAuthenticationService>();
            services.AddScoped<IAuthenticationService, HeaderAuthenticationService>();

            services.RemoveAll<IMediator>();
            services.AddSingleton(CreateMediator());
        });
    }

    private static IMediator CreateMediator()
    {
        var mediator = Substitute.For<IMediator>();
        var credential = new IntegrationCredentialDto(
            "int_01JTESTCREDENTIAL0000000000",
            "github:test",
            IntegrationCredentialType.GitHubPat,
            "GitHub test",
            null,
            null,
            new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero),
            null,
            null);

        mediator.Send(Arg.Any<ListIntegrationCredentialsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<IntegrationCredentialDto>>([credential])));
        mediator.Send(Arg.Any<CreateIntegrationCredentialCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(credential)));
        mediator.Send(Arg.Any<DeleteIntegrationCredentialCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));
        mediator.Send(Arg.Any<RotateIntegrationCredentialCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));
        return mediator;
    }

    private sealed class HeaderAuthenticationService : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            var caller = context.Request.Headers[CallerHeader].ToString();
            var requestedScheme = scheme ?? AuthSchemes.Cookie;
            if (caller == "api-key")
            {
                return Task.FromResult(requestedScheme == AuthSchemes.ApiKey
                    ? Success(requestedScheme,
                        new Claim(ClaimTypes.NameIdentifier, "apk_test"),
                        new Claim(ApiKeyAuthSchemes.ApiKeyIdClaim, "apk_test"),
                        new Claim(ApiKeyAuthSchemes.ScopeClaim, ApiKey.AdminScope))
                    : AuthenticateResult.NoResult());
            }

            if (requestedScheme != AuthSchemes.Cookie || caller is not ("ordinary" or "admin"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, caller == "admin" ? "usr_admin" : "usr_ordinary"),
                new(ClaimTypes.Name, caller),
                new(ClaimTypes.Role, caller == "admin" ? Role.AdminSlug : Role.ViewerSlug),
            };
            return Task.FromResult(Success(requestedScheme, [.. claims]));
        }

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties) => throw new NotSupportedException();

        public Task SignOutAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties) => throw new NotSupportedException();

        private static AuthenticateResult Success(string scheme, params Claim[] claims)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
            return AuthenticateResult.Success(new AuthenticationTicket(principal, scheme));
        }
    }
}

public sealed class CaptureAuditLoggerProvider : ILoggerProvider
{
    private const string AuditCategory = "Aethra.Settings.IntegrationCredentials.Audit";
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, _entries);

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }
    }

    public void Dispose()
    {
    }

    private sealed class CaptureLogger(string categoryName, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => categoryName == AuditCategory;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                entries.Enqueue(formatter(state, exception));
            }
        }
    }
}
