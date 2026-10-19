using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Modules.Identity.Presentation;
using Aethra.Modules.Identity.UseCases.Commands;
using Aethra.Shared.Contracts.Authentication;
using Aethra.Shared.Infrastructure.Pipelines;
using Aethra.Shared.Kernel.Time;
using FluentAssertions;
using FluentValidation;
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

namespace Aethra.Modules.Identity.Tests;

public sealed class ScopeGrantAuthorizationTests
{
    private const string TestScheme = "scope-dominance-test";
    private const string CallerKindHeader = "X-Test-Caller-Kind";
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly User TargetUser = User.Create("target@example.com", [1, 2, 3], null, Now);
    private static readonly Claim[] UsersWriteClaims =
        [new(ApiKeyAuthSchemes.ScopeClaim, "users:write")];
    private static readonly Claim[] AdminClaims =
    [
        new(ClaimTypes.Role, Role.AdminSlug),
        new(ApiKeyAuthSchemes.ScopeClaim, ApiKey.AdminScope),
    ];
    private static readonly string[] EscalatedRoleSlugs = ["escalated"];

    public static TheoryData<string, string, string> EscalationMatrix => new()
    {
        { "create-role", "cookie", ApiKey.AdminScope },
        { "create-role", "api-key", "services:write" },
        { "update-role", "cookie", "services:write" },
        { "update-role", "api-key", ApiKey.AdminScope },
        { "assign-role", "cookie", ApiKey.AdminScope },
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

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        body.RootElement.GetProperty("code").GetString()
            .Should().Be(ScopeGrantAuthorization.ForbiddenErrorCode);
    }

    [Theory]
    [InlineData("cookie")]
    [InlineData("api-key")]
    public async Task Post_users_cannot_create_a_user_with_the_admin_role(string callerKind)
    {
        await using var app = await BuildAppAsync(ApiKey.AdminScope);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/users/")
        {
            Content = JsonContent.Create(new
            {
                email = "new-admin@example.com",
                password = "change-me-1234",
                roleSlugs = new[] { Role.AdminSlug },
            }),
        };
        request.Headers.Add(CallerKindHeader, callerKind);

        using var response = await client.SendAsync(request);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        body.RootElement.GetProperty("code").GetString()
            .Should().Be(ScopeGrantAuthorization.ForbiddenErrorCode);
    }

    [Theory]
    [InlineData("POST", "/api/identity/roles/")]
    [InlineData("PATCH", "/api/identity/roles/rol_test")]
    public async Task Null_scopes_are_reported_as_validation_instead_of_server_error(
        string method,
        string path)
    {
        await using var app = await BuildAppAsync("users:write");
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new
            {
                slug = "ops",
                displayName = "Ops",
                scopes = (string[]?)null,
            }),
        };
        request.Headers.Add(CallerKindHeader, "api-key");

        using var response = await client.SendAsync(request);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        body.RootElement.GetProperty("code").GetString().Should().Be("validation.failed");
    }

    [Theory]
    [InlineData("cookie")]
    [InlineData("api-key")]
    public void Caller_can_grant_only_the_scopes_it_already_has(string callerKind)
    {
        var caller = Principal(callerKind, "users:write");

        ScopeGrantAuthorization.Validate(caller, ["users:write"]).Should().BeNull();
        var error = ScopeGrantAuthorization.Validate(caller, ["users:write", "services:write"]);
        error.Should().NotBeNull();
        error!.Code.Should().Be(ScopeGrantAuthorization.ForbiddenErrorCode);
    }

    [Theory]
    [InlineData("cookie")]
    [InlineData("api-key")]
    public void Administrators_can_grant_any_scope(string callerKind)
    {
        var caller = Principal(callerKind, ApiKey.AdminScope);

        ScopeGrantAuthorization.Validate(caller, [ApiKey.AdminScope, "services:write"])
            .Should().BeNull();
    }

    [Fact]
    public void Null_granted_scopes_defers_to_validator_without_throwing()
    {
        var caller = Principal("api-key", "users:write");
        ScopeGrantAuthorization.Validate(caller, null).Should().BeNull();
    }

    [Theory]
    [InlineData("POST", "/api/identity/roles/")]
    [InlineData("POST", "/api/identity/users/")]
    public async Task Administrators_are_not_rejected_by_scope_dominance(
        string method,
        string path)
    {
        await using var app = await BuildAppAsync(ApiKey.AdminScope);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = path.Contains("roles", StringComparison.Ordinal)
                ? JsonContent.Create(new
                {
                    slug = "escalated",
                    displayName = "Escalated",
                    scopes = new[] { ApiKey.AdminScope },
                })
                : JsonContent.Create(new
                {
                    email = "existing@example.com",
                    password = "change-me-1234",
                    roleSlugs = new[] { Role.AdminSlug },
                }),
        };
        request.Headers.Add(CallerKindHeader, "api-key-admin");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "el administrador supera la autorización de dominancia y alcanza la validación de conflicto de negocio");
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

    private static async Task<WebApplication> BuildAppAsync(string grantedScope)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services
            .AddAuthentication(TestScheme)
            .AddScheme<AuthenticationSchemeOptions, UsersWriteHandler>(TestScheme, _ => { });
        builder.Services.AddAuthorization(options => options.AddApiKeyScopePolicies());
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentPrincipalAccessor, CurrentPrincipalAccessor>();
        builder.Services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused"));

        var role = Role.CreateCustom("escalated", "Escalated", [grantedScope], Now);
        var admin = Role.CreateSystem(Role.AdminSlug, "Admin", [ApiKey.AdminScope], Now);
        var roles = Substitute.For<IRoleRepository>();
        roles.FindBySlugAsync("escalated", Arg.Any<CancellationToken>()).Returns(role);
        roles.FindBySlugAsync(Role.AdminSlug, Arg.Any<CancellationToken>()).Returns(admin);
        builder.Services.AddSingleton(roles);

        var users = Substitute.For<IUserRepository>();
        users.FindByEmailAsync("existing@example.com", Arg.Any<CancellationToken>()).Returns(TargetUser);
        users.FindByEmailAsync(Arg.Is<string>(e => e != "existing@example.com"), Arg.Any<CancellationToken>()).Returns((User?)null);
        users.GetByIdAsync(TargetUser.Id, Arg.Any<CancellationToken>()).Returns(TargetUser);
        builder.Services.AddSingleton(users);
        builder.Services.AddSingleton(Substitute.For<IUserPasswordCodec>());
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        builder.Services.AddSingleton(clock);

        builder.Services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(CreateUserCommand).Assembly);
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        builder.Services.AddValidatorsFromAssembly(typeof(CreateUserCommand).Assembly);

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
                    slug = "escalated-new",
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
            "assign-role" => new HttpRequestMessage(
                HttpMethod.Patch,
                $"/api/identity/users/{TargetUser.Id}")
            {
                Content = JsonContent.Create(new { roleSlugs = EscalatedRoleSlugs }),
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
