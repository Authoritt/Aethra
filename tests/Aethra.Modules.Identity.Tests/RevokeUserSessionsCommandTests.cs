using System.Security.Claims;
using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Modules.Identity.UseCases.Commands;
using Aethra.Shared.Contracts.Authentication;
using Aethra.Shared.Kernel.Time;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

public sealed class RevokeUserSessionsCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Revoke_sessions_rotates_stamp_and_persists()
    {
        await using var db = CreateDb();
        var user = User.Create("alice@example.com", [1, 2, 3], "Alice", Now);
        var initialStamp = user.SecurityStamp;

        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var roles = Substitute.For<IRoleRepository>();
        roles.ListByIdsAsync(Arg.Any<IEnumerable<RoleId>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<Role>>([]);

        var handler = new RevokeUserSessionsHandler(
            db, users, roles, Clock(), Principal(ApiKey.AdminScope));

        var result = await handler.Handle(
            new RevokeUserSessionsCommand(user.Id.ToString()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(user.Id.ToString());
        result.Value.Revoked.Should().BeTrue();
        user.SecurityStamp.Should().NotBe(initialStamp);
    }

    [Fact]
    public async Task Revoke_sessions_returns_not_found_for_invalid_or_missing_user()
    {
        await using var db = CreateDb();
        var users = Substitute.For<IUserRepository>();
        var roles = Substitute.For<IRoleRepository>();
        var handler = new RevokeUserSessionsHandler(
            db, users, roles, Clock(), Principal(ApiKey.AdminScope));

        var resultInvalid = await handler.Handle(
            new RevokeUserSessionsCommand("not_a_valid_id"),
            CancellationToken.None);
        resultInvalid.IsSuccess.Should().BeFalse();
        resultInvalid.Error.Code.Should().Be("user.not_found");

        var resultNotFound = await handler.Handle(
            new RevokeUserSessionsCommand(UserId.New().ToString()),
            CancellationToken.None);
        resultNotFound.IsSuccess.Should().BeFalse();
        resultNotFound.Error.Code.Should().Be("user.not_found");
    }

    [Fact]
    public async Task Revoke_sessions_rejects_caller_with_insufficient_scopes_against_target_user()
    {
        await using var db = CreateDb();
        var user = User.Create("admin@example.com", [1, 2, 3], "Admin", Now);
        var adminRole = Role.CreateSystem(Role.AdminSlug, "Admin", [ApiKey.AdminScope], Now);
        user.AssignRole(adminRole.Id, Now);

        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var roles = Substitute.For<IRoleRepository>();
        roles.ListByIdsAsync(Arg.Any<IEnumerable<RoleId>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<Role>>([adminRole]);

        // Caller solo tiene "users:write", pero el usuario objetivo tiene "*" (Admin)
        var handler = new RevokeUserSessionsHandler(
            db, users, roles, Clock(), Principal("users:write"));

        var result = await handler.Handle(
            new RevokeUserSessionsCommand(user.Id.ToString()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(ScopeGrantAuthorization.ForbiddenErrorCode);
    }

    [Fact]
    public async Task Revoke_sessions_allows_self_revocation_even_with_stronger_roles()
    {
        await using var db = CreateDb();
        var user = User.Create("admin@example.com", [1, 2, 3], "Admin", Now);
        var adminRole = Role.CreateSystem(Role.AdminSlug, "Admin", [ApiKey.AdminScope], Now);
        user.AssignRole(adminRole.Id, Now);

        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var roles = Substitute.For<IRoleRepository>();

        // Caller es el propio usuario autenticado
        var handler = new RevokeUserSessionsHandler(
            db, users, roles, Clock(), PrincipalWithUser(user.Id.ToString(), "users:write"));

        var result = await handler.Handle(
            new RevokeUserSessionsCommand(user.Id.ToString()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    private static IdentityDbContext CreateDb()
        => new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options);

    private static IClock Clock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return clock;
    }

    private static CurrentPrincipalAccessor Principal(params string[] scopes)
    {
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    scopes.Select(scope => new Claim(ApiKeyAuthSchemes.ScopeClaim, scope)),
                    "test")),
            },
        };
        return new CurrentPrincipalAccessor(http);
    }

    private static CurrentPrincipalAccessor PrincipalWithUser(string userId, params string[] scopes)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
        };
        claims.AddRange(scopes.Select(scope => new Claim(ApiKeyAuthSchemes.ScopeClaim, scope)));

        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
            },
        };
        return new CurrentPrincipalAccessor(http);
    }
}
