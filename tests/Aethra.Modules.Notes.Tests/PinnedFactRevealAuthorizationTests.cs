using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using Aethra.Modules.Notes.Application.Dtos;
using Aethra.Modules.Notes.Application.Queries;
using Aethra.Modules.Notes.Domain;
using Aethra.Modules.Notes.Infrastructure;
using Aethra.Modules.Notes.Presentation;
using Aethra.Shared.Contracts.Authentication;
using Aethra.Shared.Contracts.Notes;
using Aethra.Shared.Kernel.Results;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Aethra.Modules.Notes.Tests;

public sealed class PinnedFactRevealAuthorizationTests
{
    private const string TestScheme = "pinned-fact-auth";
    private const string CallerHeader = "X-Test-Caller";
    private const string SecretValue = "super-secret-value";

    [Theory]
    [InlineData("cookie-reader")]
    [InlineData("api-key-reader")]
    public async Task Notes_read_can_list_masked_secret_metadata(string caller)
    {
        await using var fixture = await BuildAppAsync();
        using var request = Request(caller, reveal: false);

        using var response = await fixture.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("admin_password");
        body.Should().Contain("********");
        body.Should().NotContain(SecretValue);
        await fixture.Mediator.Received(1).Send(
            Arg.Is<ListPinnedFactsQuery>(q => !q.IncludeSecretValues),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("cookie-reader")]
    [InlineData("api-key-reader")]
    public async Task Notes_read_alone_cannot_reveal_secret_values(string caller)
    {
        await using var fixture = await BuildAppAsync();
        using var request = Request(caller, reveal: true);

        using var response = await fixture.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await fixture.Mediator.DidNotReceive().Send(
            Arg.Any<ListPinnedFactsQuery>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("cookie-admin")]
    [InlineData("api-key-writer")]
    public async Task Explicit_stronger_authorization_can_reveal_secret_values(string caller)
    {
        await using var fixture = await BuildAppAsync();
        using var request = Request(caller, reveal: true);

        using var response = await fixture.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain(SecretValue);
        await fixture.Mediator.Received(1).Send(
            Arg.Is<ListPinnedFactsQuery>(q => q.IncludeSecretValues),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reveal_audit_logs_the_decision_without_the_secret_value()
    {
        await using var fixture = await BuildAppAsync();
        using var request = Request("api-key-writer", reveal: true);

        using var response = await fixture.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        fixture.Logs.Should().Contain(message => message.Contains(
            "Pinned fact secret reveal authorized", StringComparison.Ordinal));
        fixture.Logs.Should().OnlyContain(message =>
            !message.Contains(SecretValue, StringComparison.Ordinal));
    }

    private static HttpRequestMessage Request(string caller, bool reveal)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/pinned-facts/?scope_type=project&scope_id=prj_1&reveal={reveal.ToString().ToLowerInvariant()}");
        request.Headers.Add(CallerHeader, caller);
        return request;
    }

    private static async Task<Fixture> BuildAppAsync()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<ListPinnedFactsQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var query = call.Arg<ListPinnedFactsQuery>();
                IReadOnlyList<PinnedFactDto> facts =
                [
                    new(
                        "pf_test",
                        query.ScopeType.ToString(),
                        query.ScopeId,
                        "admin_password",
                        query.IncludeSecretValues ? SecretValue : "********",
                        IsSecret: true,
                        Description: "Database credential",
                        CreatedAt: DateTimeOffset.UnixEpoch,
                        UpdatedAt: DateTimeOffset.UnixEpoch),
                ];
                return Result.Success(facts);
            });

        var logs = new List<string>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new RecordingLoggerProvider(logs));
        builder.Services
            .AddAuthentication(TestScheme)
            .AddScheme<AuthenticationSchemeOptions, CallerAuthenticationHandler>(TestScheme, _ => { });
        builder.Services.AddAuthorization(options => options.AddApiKeyScopePolicies());
        builder.Services.AddSingleton(mediator);
        builder.Services.AddDbContext<NotesDbContext>(options => options.UseNpgsql(
            "Host=unused;Database=unused;Username=unused;Password=unused"));
        builder.Services.AddSingleton(Substitute.For<INoteImageStore>());

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapNotesEndpoints();
        await app.StartAsync();

        return new Fixture(app, app.GetTestClient(), mediator, logs);
    }

    private sealed class CallerAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var caller = Request.Headers[CallerHeader].ToString();
            if (string.IsNullOrWhiteSpace(caller))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>();
            if (caller == "cookie-admin")
            {
                claims.Add(new Claim(ClaimTypes.Role, Role.AdminSlug));
            }
            else
            {
                claims.Add(new Claim(ApiKeyAuthSchemes.ScopeClaim, "notes:read"));
                if (caller == "api-key-writer")
                {
                    claims.Add(new Claim(ApiKeyAuthSchemes.ScopeClaim, "notes:write"));
                }
            }

            var identity = new ClaimsIdentity(claims, caller.StartsWith("cookie", StringComparison.Ordinal)
                ? ApiKeyAuthSchemes.CookieScheme
                : "aethra.apikey");
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class RecordingLoggerProvider(List<string> messages) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new RecordingLogger(messages);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => messages.Add(formatter(state, exception));
        }
    }

    private sealed record Fixture(
        WebApplication App,
        HttpClient Client,
        IMediator Mediator,
        List<string> Logs) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
        }
    }
}
