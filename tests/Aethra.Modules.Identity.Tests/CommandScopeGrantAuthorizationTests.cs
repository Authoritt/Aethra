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

public sealed class CommandScopeGrantAuthorizationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_role_handler_rejects_wildcard_for_users_write_only()
    {
        await using var db = CreateDb();
        var roles = Substitute.For<IRoleRepository>();
        var handler = new CreateRoleHandler(db, roles, Clock(), Principal("users:write"));

        var result = await handler.Handle(
            new CreateRoleCommand("root", "Root", [ApiKey.AdminScope]),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(ScopeGrantAuthorization.ForbiddenErrorCode);
        await roles.DidNotReceiveWithAnyArgs().FindBySlugAsync(default!, default);
    }

    [Fact]
    public async Task Update_role_handler_rejects_wildcard_for_users_write_only()
    {
        await using var db = CreateDb();
        var roles = Substitute.For<IRoleRepository>();
        var handler = new UpdateRoleHandler(db, roles, Clock(), Principal("users:write"));

        var result = await handler.Handle(
            new UpdateRoleCommand("rol_any", "Root", [ApiKey.AdminScope]),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(ScopeGrantAuthorization.ForbiddenErrorCode);
        await roles.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task Create_user_handler_rejects_an_initial_admin_role_for_users_write_only()
    {
        await using var db = CreateDb();
        var users = Substitute.For<IUserRepository>();
        users.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        var roles = Substitute.For<IRoleRepository>();
        roles.FindBySlugAsync(Role.AdminSlug, Arg.Any<CancellationToken>())
            .Returns(Role.CreateSystem(Role.AdminSlug, "Admin", [ApiKey.AdminScope], Now));
        var passwords = Substitute.For<IUserPasswordCodec>();
        var handler = new CreateUserHandler(
            db, users, roles, passwords, Clock(), Principal("users:write"));

        var result = await handler.Handle(
            new CreateUserCommand(
                "new-admin@example.com",
                "change-me-1234",
                null,
                [Role.AdminSlug]),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(ScopeGrantAuthorization.ForbiddenErrorCode);
        passwords.DidNotReceiveWithAnyArgs().HashAndProtect(default!);
    }

    [Fact]
    public async Task Update_user_handler_rejects_assigning_a_stronger_role_for_users_write_only()
    {
        await using var db = CreateDb();
        var user = User.Create("target@example.com", [1, 2, 3], null, Now);
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var roles = Substitute.For<IRoleRepository>();
        roles.FindBySlugAsync("root", Arg.Any<CancellationToken>())
            .Returns(Role.CreateCustom("root", "Root", [ApiKey.AdminScope], Now));
        var handler = new UpdateUserHandler(db, users, roles, Clock(), Principal("users:write"));

        var result = await handler.Handle(
            new UpdateUserCommand(user.Id.ToString(), null, ["root"]),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(ScopeGrantAuthorization.ForbiddenErrorCode);
        user.Roles.Should().BeEmpty();
    }

    [Fact]
    public async Task Wildcard_principal_passes_command_layer_authorization()
    {
        await using var db = CreateDb();
        var existing = Role.CreateCustom("root", "Root", [ApiKey.AdminScope], Now);
        var roles = Substitute.For<IRoleRepository>();
        roles.FindBySlugAsync("root", Arg.Any<CancellationToken>()).Returns(existing);
        var handler = new CreateRoleHandler(db, roles, Clock(), Principal(ApiKey.AdminScope));

        var result = await handler.Handle(
            new CreateRoleCommand("root", "Root", [ApiKey.AdminScope]),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("role.slug_in_use",
            "el wildcard debe atravesar la autorización y alcanzar la validación de negocio");
    }

    [Fact]
    public async Task Wildcard_principal_passes_update_role_authorization()
    {
        await using var db = CreateDb();
        var roles = Substitute.For<IRoleRepository>();
        var handler = new UpdateRoleHandler(db, roles, Clock(), Principal(ApiKey.AdminScope));

        var result = await handler.Handle(
            new UpdateRoleCommand("invalid_id", "Root", [ApiKey.AdminScope]),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("role.not_found",
            "el wildcard debe atravesar la autorización y alcanzar la validación de id");
    }

    [Fact]
    public async Task Wildcard_principal_passes_create_user_authorization()
    {
        await using var db = CreateDb();
        var users = Substitute.For<IUserRepository>();
        var existingUser = User.Create("existing@example.com", [1], null, Now);
        users.FindByEmailAsync("existing@example.com", Arg.Any<CancellationToken>()).Returns(existingUser);
        var roles = Substitute.For<IRoleRepository>();
        roles.FindBySlugAsync(Role.AdminSlug, Arg.Any<CancellationToken>())
            .Returns(Role.CreateSystem(Role.AdminSlug, "Admin", [ApiKey.AdminScope], Now));
        var passwords = Substitute.For<IUserPasswordCodec>();
        var handler = new CreateUserHandler(
            db, users, roles, passwords, Clock(), Principal(ApiKey.AdminScope));

        var result = await handler.Handle(
            new CreateUserCommand(
                "existing@example.com",
                "change-me-1234",
                null,
                [Role.AdminSlug]),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("user.email_in_use",
            "el wildcard debe atravesar la autorización y alcanzar la validación de email");
    }

    [Fact]
    public async Task Null_scopes_in_role_commands_defer_to_validator_without_throwing()
    {
        await using var db = CreateDb();
        var existing = Role.CreateCustom("root", "Root", ["users:write"], Now);
        var roles = Substitute.For<IRoleRepository>();
        roles.FindBySlugAsync("root", Arg.Any<CancellationToken>()).Returns(existing);
        var handler = new CreateRoleHandler(db, roles, Clock(), Principal("users:write"));

        var result = await handler.Handle(
            new CreateRoleCommand("root", "Root", null!),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("role.slug_in_use",
            "scopes null no debe arrojar ArgumentNullException en autorización");
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
}
