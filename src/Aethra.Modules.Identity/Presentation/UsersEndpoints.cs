using Aethra.Modules.Identity.UseCases.Commands;
using Aethra.Modules.Identity.UseCases.Queries;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Aethra.Modules.Identity.Presentation;

public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        // F11.1: scope users:read para list/inspect, users:write para mutaciones.
        // La cookie del admin (o keys con scope *) cubre ambos.
        var users = app.MapGroup("/api/identity/users")
            .WithTags("Identity");

        users.MapGet("/", async (IMediator m, CancellationToken ct) =>
                ToResult(await m.Send(new ListUsersQuery(), ct)))
            .WithName("ListUsers")
            .RequireAuthorization("scope:users:read");

        users.MapPost("/", async ([FromBody] CreateUserRequest body, IMediator m, CancellationToken ct) =>
            {
                var cmd = new CreateUserCommand(body.Email, body.Password, body.DisplayName, body.RoleSlugs);
                var r = await m.Send(cmd, ct);
                return r.IsSuccess
                    ? Results.Created($"/api/identity/users/{r.Value.Id}", r.Value)
                    : MapError(r.Error);
            })
            .WithName("CreateUser")
            .RequireAuthorization("scope:users:write");

        users.MapPatch("/{userId}", async (string userId, [FromBody] UpdateUserRequest body, IMediator m, CancellationToken ct) =>
            {
                var r = await m.Send(
                    new UpdateUserCommand(
                        userId,
                        body.DisplayName,
                        body.RoleSlugs,
                        body.GitHubUsername,
                        body.ClearGitHubUsername ?? false),
                    ct);
                return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
            })
            .WithName("UpdateUser")
            .RequireAuthorization("scope:users:write");

        users.MapPost("/{userId}/reset-password", async (string userId, [FromBody] ResetPasswordRequest body, IMediator m, CancellationToken ct) =>
            {
                var r = await m.Send(new ResetUserPasswordCommand(userId, body.NewPassword), ct);
                return r.IsSuccess ? Results.Ok(r.Value) : MapError(r.Error);
            })
            .WithName("ResetUserPassword")
            .RequireAuthorization("scope:users:write");

        users.MapDelete("/{userId}", async (string userId, IMediator m, CancellationToken ct) =>
            {
                var r = await m.Send(new DeactivateUserCommand(userId), ct);
                return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
            })
            .WithName("DeactivateUser")
            .RequireAuthorization("scope:users:write");

        // ---- Roles ----
        var roles = app.MapGroup("/api/identity/roles")
            .WithTags("Identity");

        roles.MapGet("/", async (IMediator m, CancellationToken ct) =>
                ToResult(await m.Send(new ListRolesQuery(), ct)))
            .WithName("ListRoles")
            .RequireAuthorization("scope:users:read");

        roles.MapPost("/", async ([FromBody] CreateRoleRequest body, IMediator m, CancellationToken ct) =>
            {
                var cmd = new CreateRoleCommand(body.Slug, body.DisplayName, body.Scopes);
                var r = await m.Send(cmd, ct);
                return r.IsSuccess
                    ? Results.Created($"/api/identity/roles/{r.Value.Id}", r.Value)
                    : MapError(r.Error);
            })
            .WithName("CreateRole")
            .RequireAuthorization("scope:users:write");

        roles.MapPatch("/{roleId}", async (string roleId, [FromBody] UpdateRoleRequest body, IMediator m, CancellationToken ct) =>
            {
                var cmd = new UpdateRoleCommand(roleId, body.DisplayName, body.Scopes);
                var r = await m.Send(cmd, ct);
                return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
            })
            .WithName("UpdateRole")
            .RequireAuthorization("scope:users:write");

        roles.MapDelete("/{roleId}", async (string roleId, IMediator m, CancellationToken ct) =>
            {
                var r = await m.Send(new DeleteRoleCommand(roleId), ct);
                return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
            })
            .WithName("DeleteRole")
            .RequireAuthorization("scope:users:write");

        return app;
    }

    public sealed record CreateUserRequest(
        string Email,
        string Password,
        string? DisplayName,
        IReadOnlyList<string> RoleSlugs);

    public sealed record UpdateUserRequest(
        string? DisplayName,
        IReadOnlyList<string>? RoleSlugs,
        string? GitHubUsername = null,
        bool? ClearGitHubUsername = null);

    public sealed record ResetPasswordRequest(string NewPassword);

    public sealed record CreateRoleRequest(
        string Slug,
        string DisplayName,
        IReadOnlyList<string> Scopes);

    public sealed record UpdateRoleRequest(
        string DisplayName,
        IReadOnlyList<string> Scopes);

    private static IResult ToResult<T>(Result<T> r)
        => r.IsSuccess ? Results.Ok(r.Value) : MapError(r.Error);

    private static IResult MapError(Error e) => e.Type switch
    {
        ErrorType.Validation => Results.UnprocessableEntity(new { e.Code, e.Message }),
        ErrorType.NotFound => Results.NotFound(new { e.Code, e.Message }),
        ErrorType.Conflict => Results.Conflict(new { e.Code, e.Message }),
        ErrorType.Forbidden => Results.Json(new { e.Code, e.Message }, statusCode: StatusCodes.Status403Forbidden),
        _ => Results.Problem(e.Message),
    };
}
