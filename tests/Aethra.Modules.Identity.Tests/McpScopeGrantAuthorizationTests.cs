using System.Text.Json;
using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using Aethra.Modules.Identity.UseCases.Commands;
using Aethra.Modules.Identity.UseCases.Dtos;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Mcp.Tools;
using Aethra.Shared.Kernel.Results;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

public sealed class McpScopeGrantAuthorizationTests
{
    private const string UsersWrite = "users:write";
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Mutations =>
        ["create-role", "update-role", "assign-role", "create-user"];

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task Users_write_api_key_cannot_grant_stronger_roles_through_mcp(string mutation)
    {
        var (identityTools, adminTools) = CreateTools(new HashSet<string>([UsersWrite]));

        var response = await InvokeAsync(mutation, identityTools, adminTools);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response));

        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be(ScopeGrantAuthorization.ForbiddenErrorCode);
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task Wildcard_api_key_can_grant_stronger_roles_through_mcp(string mutation)
    {
        var (identityTools, adminTools) = CreateTools(new HashSet<string>([ApiKey.AdminScope]));

        var response = await InvokeAsync(mutation, identityTools, adminTools);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response));

        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
    }

    private static (IdentityTools Identity, AdminCrudTools Admin) CreateTools(
        HashSet<string> callerScopes)
    {
        var accessor = new CurrentPrincipalAccessor(new HttpContextAccessor());
        var caller = Substitute.For<IMcpCallerContext>();
        caller.Scopes.Returns(callerScopes);
        caller.HasScope(Arg.Any<string>()).Returns(call =>
            callerScopes.Contains(ApiKey.AdminScope) || callerScopes.Contains(call.Arg<string>()));

        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<CreateRoleCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => CreateRoleResult(accessor, call.Arg<CreateRoleCommand>()));
        mediator.Send(Arg.Any<UpdateRoleCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => ResultFor(accessor, call.Arg<UpdateRoleCommand>().Scopes));
        mediator.Send(Arg.Any<CreateUserCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateUserResult(accessor));
        mediator.Send(Arg.Any<UpdateUserCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => ResultFor(accessor, [ApiKey.AdminScope]));
        mediator.Send(Arg.Any<Aethra.Modules.Identity.UseCases.Queries.ListUsersQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<UserSummaryDto>>(
            [
                new UserSummaryDto(
                    "usr_target",
                    "target@example.com",
                    null,
                    [],
                    true,
                    null,
                    Now,
                    Now),
            ]));

        return (
            new IdentityTools(mediator, caller, accessor),
            new AdminCrudTools(mediator, caller, accessor));
    }

    private static Result<CreatedRoleDto> CreateRoleResult(
        CurrentPrincipalAccessor accessor,
        CreateRoleCommand command)
    {
        var error = ScopeGrantAuthorization.Validate(accessor.Current, command.Scopes);
        return error is null
            ? Result.Success(new CreatedRoleDto("rol_root", command.Slug, command.DisplayName, command.Scopes))
            : Result<CreatedRoleDto>.Failure(error);
    }

    private static Result<CreatedUserDto> CreateUserResult(CurrentPrincipalAccessor accessor)
    {
        var error = ScopeGrantAuthorization.Validate(accessor.Current, [ApiKey.AdminScope]);
        return error is null
            ? Result.Success(new CreatedUserDto("usr_new", "new@example.com", null, []))
            : Result<CreatedUserDto>.Failure(error);
    }

    private static Result ResultFor(
        CurrentPrincipalAccessor accessor,
        IReadOnlyList<string> grantedScopes)
    {
        var error = ScopeGrantAuthorization.Validate(accessor.Current, grantedScopes);
        return error is null ? Result.Success() : Result.Failure(error);
    }

    private static Task<object> InvokeAsync(
        string mutation,
        IdentityTools identityTools,
        AdminCrudTools adminTools)
        => mutation switch
        {
            "create-role" => identityTools.CreateCustomRoleAsync(
                "root", "Root", [ApiKey.AdminScope], false, CancellationToken.None),
            "update-role" => adminTools.UpdateRoleAsync(
                "rol_root", "Root", [ApiKey.AdminScope], CancellationToken.None),
            "assign-role" => identityTools.AssignRoleAsync(
                "usr_target", "admin", false, CancellationToken.None),
            "create-user" => identityTools.CreateUserAsync(
                "new@example.com", "change-me-1234", [Role.AdminSlug], null, false, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };
}
