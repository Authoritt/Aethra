using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.UseCases.Backups;
using Aethra.Modules.Services.UseCases.Commands;
using Aethra.Modules.Services.UseCases.Queries;
using Aethra.Shared.Contracts.Services;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Aethra.Modules.Services.Presentation;

public static class ServicesEndpoints
{
    // Bindings forman parte del dominio Services — reusamos sus scopes.
    private const string ScopeRead = "scope:services:read";
    private const string ScopeWrite = "scope:services:write";

    public static IEndpointRouteBuilder MapServicesEndpoints(this IEndpointRouteBuilder app)
    {
        var services = app.MapGroup("/api/services").WithTags("Services");

        services.MapGet("/templates", async (IMediator m, CancellationToken ct) =>
            ToResult(await m.Send(new ListTemplatesQuery(), ct)))
            .RequireAuthorization(ScopeRead)
            .WithName("ListServiceTemplates");

        services.MapGet("/", async (IMediator m, CancellationToken ct) =>
            ToResult(await m.Send(new ListServicesQuery(), ct)))
            .RequireAuthorization(ScopeRead)
            .WithName("ListServices");

        services.MapGet("/{serviceId}", async (string serviceId, IMediator m, CancellationToken ct) =>
            ToResult(await m.Send(new GetServiceByIdQuery(serviceId), ct)))
            .RequireAuthorization(ScopeRead)
            .WithName("GetService");

        services.MapPost("/", async ([FromBody] CreateServiceRequest body, IMediator m, CancellationToken ct) =>
        {
            var cmd = new CreateServiceFromTemplateCommand(
                TemplateId: body.TemplateId,
                Slug: body.Slug,
                Name: body.Name,
                TargetVmId: body.TargetVmId,
                ExposedExternally: body.ExposedExternally ?? false);
            var r = await m.Send(cmd, ct);
            return r.IsSuccess
                ? Results.Created($"/api/services/{r.Value.Id}", r.Value)
                : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("CreateServiceFromTemplate");

        services.MapDelete("/{serviceId}", async (string serviceId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new DeleteServiceCommand(serviceId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("DeleteService");

        services.MapGet("/{serviceId}/bindings", async (string serviceId, [FromQuery] bool? includeRevoked,
            IMediator m, CancellationToken ct) =>
            ToResult(await m.Send(new ListBindingsQuery(serviceId, includeRevoked ?? false), ct)))
            .RequireAuthorization(ScopeRead)
            .WithName("ListBindings");

        services.MapPost("/{serviceId}/bindings", async (string serviceId, [FromBody] CreateBindingRequest body,
            IMediator m, CancellationToken ct) =>
        {
            if (!Enum.TryParse<BindingPermissions>(body.Permissions, ignoreCase: true, out var perms))
            {
                return Results.UnprocessableEntity(new
                {
                    Code = "binding.invalid_permissions",
                    Message = $"Permissions inválidos: '{body.Permissions}'. Use Owner|ReadWrite|ReadOnly.",
                });
            }
            MigrationsHook? hook = null;
            if (body.MigrationsHook is { } h)
            {
                if (!Enum.TryParse<MigrationsHookRunOn>(h.RunOn, ignoreCase: true, out var runOn))
                {
                    return Results.UnprocessableEntity(new
                    {
                        Code = "hook.invalid_run_on",
                        Message = $"run_on inválido: '{h.RunOn}'. Use EachDeploy|FirstDeployOnly|ManualTrigger.",
                    });
                }
                hook = new MigrationsHook(h.Command, h.TimeoutSeconds, h.FailDeployOnError, runOn);
            }
            var cmd = new CreateBindingCommand(serviceId, body.InstanceId, body.ResourceName, perms,
                body.EnvVarPrefix, hook);
            var r = await m.Send(cmd, ct);
            return r.IsSuccess
                ? Results.Created($"/api/bindings/{r.Value.Id}", r.Value)
                : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("CreateBinding");

        // Standalone bindings endpoints (acción independiente del service path).
        var bindings = app.MapGroup("/api/bindings").WithTags("Services");

        bindings.MapDelete("/{bindingId}", async (string bindingId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new RevokeBindingCommand(bindingId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("RevokeBinding");

        bindings.MapPost("/{bindingId}/rotate", async (string bindingId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new RotateCredentialsCommand(bindingId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("RotateBindingCredentials");

        // ----------- F11.3B: Backups -----------
        services.MapGet("/{serviceId}/backups", async (string serviceId, [FromQuery] int? limit,
            IMediator m, CancellationToken ct) =>
            ToResult(await m.Send(new ListBackupsQuery(serviceId, limit ?? 50), ct)))
            .RequireAuthorization(ScopeRead)
            .WithName("ListServiceBackups");

        services.MapPost("/{serviceId}/backups/run", async (string serviceId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new RunBackupCommand(serviceId), ct);
            return r.IsSuccess
                ? Results.Created($"/api/services/{serviceId}/backups/{r.Value.Id}", r.Value)
                : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("RunServiceBackup");

        services.MapPost("/{serviceId}/backups/{backupId}/restore", async (string serviceId, string backupId,
            IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new RestoreBackupCommand(serviceId, backupId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("RestoreServiceBackup");

        services.MapDelete("/{serviceId}/backups/{backupId}", async (string serviceId, string backupId,
            IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new DeleteBackupCommand(backupId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("DeleteServiceBackup");

        services.MapPut("/{serviceId}/backup-policy", async (string serviceId, [FromBody] SetBackupPolicyRequest body,
            IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new SetBackupPolicyCommand(serviceId, body.CronExpression, body.RetentionCount, body.Destination), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("SetBackupPolicy");

        return app;
    }

    public sealed record SetBackupPolicyRequest(string? CronExpression, int? RetentionCount, string? Destination);

    public sealed record CreateServiceRequest(string TemplateId, string Slug, string Name, string TargetVmId, bool? ExposedExternally);
    public sealed record CreateBindingRequest(
        string InstanceId,
        string? ResourceName,
        string Permissions,
        string? EnvVarPrefix,
        MigrationsHookRequest? MigrationsHook);
    public sealed record MigrationsHookRequest(string Command, int TimeoutSeconds, bool FailDeployOnError, string RunOn);

    private static IResult ToResult<T>(Result<T> r)
        => r.IsSuccess ? Results.Ok(r.Value) : MapError(r.Error);

    private static IResult MapError(Error e) => e.Type switch
    {
        ErrorType.Validation => Results.UnprocessableEntity(new { e.Code, e.Message }),
        ErrorType.NotFound => Results.NotFound(new { e.Code, e.Message }),
        ErrorType.Conflict => Results.Conflict(new { e.Code, e.Message }),
        _ => Results.Problem(e.Message),
    };
}
