using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Modules.Identity.Presentation;
using Aethra.Modules.Identity.UseCases.Commands;
using Aethra.Modules.Identity.UseCases.Dtos;
using Aethra.Shared.Contracts.Authentication;
using Aethra.Shared.Kernel.Results;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

public sealed class ScopeGrantAuthorizationTests
{
    private const string TestScheme = "scope-dominance-test";
    private const string CallerKindHeader = "X-Test-Caller-Kind";
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly Claim[] UsersWriteClaims =
        [new(ApiKeyAuthSchemes.ScopeClaim, "users:write")];
    private static readonly Claim[] AdminClaims =
        [new(ApiKeyAuthSchemes.ScopeClaim, ApiKey.AdminScope)];
    private static readonly string[] EscalatedRoleSlugs = ["escalated"];

    public static TheoryData<string, string, string> EscalationMatrix => new()
    {
        { "create-role", "cookie", ApiKey.AdminScope },
        { "create-role", "cookie", "services:write" },
        { "create-role", "api-key", ApiKey.AdminScope },
        { "create-role", "api-key", "services:write" },
        { "update-role", "cookie", ApiKey.AdminScope },
        { "update-role", "cookie", "services:write" },
        { "update-role", "api-key", ApiKey.AdminScope },
        { "update-role", "api-key", "services:write" },
        { "assign-role", "cookie", ApiKey.AdminScope },
        { "assign-role", "cookie", "services:write" },
        { "assign-role", "api-key", ApiKey.AdminScope },
        { "assign-role", "api-key", "services:write" },
    };

    [Theory]
    [MemberData(nameof(EscalationMatrix))]
    public async Task Users_write_only_cannot_grant_wildcard_or_unowned_scope(
        string operation,
        string callerKind,
        string grantedScope)
    {
        await using var app = await BuildAppAsync(grantedScope);
        using var client = app.GetTestClient();
        using var request = Request(operation, callerKind, grantedScope);

        using var response = await client.SendAsync(request);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"{callerKind} must be dominated in {operation}");
        body.RootElement.GetProperty("code").GetString()
            .Should().Be(ScopeGrantAuthorization.ForbiddenErrorCode);
    }

    [Theory]
    [InlineData("cookie")]
    [InlineData("api-key")]
    public void Self_assignment_cannot_add_effective_scopes_the_caller_does_not_have(string callerKind)
    {
        var caller = Principal(callerKind, "users:write");

        var error = ScopeGrantAuthorization.Validate(
            caller,
            ["users:write", "services:write"]);

        error.Should().NotBeNull();
        error!.Code.Should().Be(ScopeGrantAuthorization.ForbiddenErrorCode);
    }

    [Theory]
    [InlineData("create-role", "cookie")]
    [InlineData("update-role", "cookie")]
    [InlineData("assign-role", "cookie")]
    [InlineData("create-role", "api-key")]
    [InlineData("update-role", "api-key")]
    [InlineData("assign-role", "api-key")]
    public void Administrators_can_grant_any_scope(string operation, string callerKind)
    {
        var caller = callerKind == "cookie"
            ? CookieAdmin()
            : Principal(callerKind, ApiKey.AdminScope);

        ScopeGrantAuthorization.Validate(
                caller,
                [ApiKey.AdminScope, "services:write"])
            .Should().BeNull($"{callerKind} administrator must retain {operation}");
    }

    [Theory]
    [InlineData("create-role", "cookie")]
    [InlineData("update-role", "cookie")]
    [InlineData("assign-role", "cookie")]
    [InlineData("create-role", "api-key")]
    [InlineData("update-role", "api-key")]
    [InlineData("assign-role", "api-key")]
    public async Task Administrators_retain_all_role_management_endpoints(
        string operation,
        string callerKind)
    {
        await using var app = await BuildAppAsync(ApiKey.AdminScope);
        using var client = app.GetTestClient();
        using var request = Request(operation, callerKind + "-admin", ApiKey.AdminScope);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(operation == "create-role"
            ? HttpStatusCode.Created
            : HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("cookie")]
    [InlineData("api-key")]
    public void Caller_can_grant_the_scopes_it_already_has(string callerKind)
    {
        var caller = Principal(callerKind, "users:write");

        ScopeGrantAuthorization.Validate(caller, ["users:write"]).Should().BeNull();
    }

    [Fact]
    public void Admin_role_without_wildcard_cannot_grant_wildcard()
    {
        var caller = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, Role.AdminSlug)],
            "cookie"));

        var error = ScopeGrantAuthorization.Validate(caller, [ApiKey.AdminScope]);

        error.Should().NotBeNull();
        error!.Code.Should().Be(ScopeGrantAuthorization.ForbiddenErrorCode);
    }

    private static ClaimsPrincipal Principal(string authenticationType, params string[] scopes)
        => new(new ClaimsIdentity(
            scopes.Select(scope => new Claim(ApiKeyAuthSchemes.ScopeClaim, scope)),
            authenticationType));

    private static ClaimsPrincipal CookieAdmin()
        => new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Role, Role.AdminSlug),
                new Claim(ApiKeyAuthSchemes.ScopeClaim, ApiKey.AdminScope),
            ],
            "cookie"));

    private static async Task<WebApplication> BuildAppAsync(string grantedScope)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services
            .AddAuthentication(TestScheme)
            .AddScheme<AuthenticationSchemeOptions, UsersWriteHandler>(TestScheme, _ => { });
        builder.Services.AddAuthorization(options => options.AddApiKeyScopePolicies());
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<CreateRoleCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new CreatedRoleDto(
                "rol_test", "escalated", "Escalated", [grantedScope])));
        mediator.Send(Arg.Any<UpdateRoleCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        mediator.Send(Arg.Any<UpdateUserCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        builder.Services.AddSingleton(mediator);

        var role = Role.CreateCustom(
            "escalated",
            "Escalated",
            [grantedScope],
            Now);
        var roles = Substitute.For<IRoleRepository>();
        roles.FindBySlugAsync("escalated", Arg.Any<CancellationToken>())
            .Returns(role);
        builder.Services.AddSingleton(roles);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapUsersEndpoints();
        await app.StartAsync();
        return app;
    }

    private static HttpRequestMessage Request(string operation, string callerKind, string grantedScope)
    {
        var request = operation switch
        {
            "create-role" => new HttpRequestMessage(HttpMethod.Post, "/api/identity/roles/")
            {
                Content = JsonContent.Create(new
                {
                    slug = "escalated",
                    displayName = "Escalated",
                    scopes = new[] { grantedScope },
                }),
            },
            "update-role" => new HttpRequestMessage(HttpMethod.Patch, "/api/identity/roles/rol_test")
            {
                Content = JsonContent.Create(new
                {
                    displayName = "Escalated",
                    scopes = new[] { grantedScope },
                }),
            },
            "assign-role" => new HttpRequestMessage(HttpMethod.Patch, "/api/identity/users/self")
            {
                Content = JsonContent.Create(new
                {
                    roleSlugs = EscalatedRoleSlugs,
                }),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        request.Headers.Add(CallerKindHeader, callerKind);
        return request;
    }

    private sealed class UsersWriteHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var callerKind = Request.Headers[CallerKindHeader].ToString();
            if (string.IsNullOrWhiteSpace(callerKind))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                callerKind.EndsWith("-admin", StringComparison.Ordinal)
                    ? AdminClaims
                    : UsersWriteClaims,
                callerKind);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
