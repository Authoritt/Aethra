using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.UseCases.Commands;
using Aethra.Modules.Services.UseCases.Queries;
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
    public static IEndpointRouteBuilder MapServicesEndpoints(this IEndpointRouteBuilder app)
    {
        var services = app.MapGroup("/api/services").WithTags("Services").RequireAuthorization();

        services.MapGet("/templates", async (IMediator m, CancellationToken ct) =>
            ToResult(await m.Send(new ListTemplatesQuery(), ct)))
            .WithName("ListServiceTemplates");

        services.MapGet("/", async (IMediator m, CancellationToken ct) =>
            ToResult(await m.Send(new ListServicesQuery(), ct)))
            .WithName("ListServices");

        services.MapGet("/{serviceId}", async (string serviceId, IMediator m, CancellationToken ct) =>
            ToResult(await m.Send(new GetServiceByIdQuery(serviceId), ct)))
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
        }).WithName("CreateServiceFromTemplate");

        services.MapDelete("/{serviceId}", async (string serviceId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new DeleteServiceCommand(serviceId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithName("DeleteService");

        services.MapGet("/{serviceId}/bindings", async (string serviceId, [FromQuery] bool? includeRevoked,
            IMediator m, CancellationToken ct) =>
            ToResult(await m.Send(new ListBindingsQuery(serviceId, includeRevoked ?? false), ct)))
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
            var cmd = new CreateBindingCommand(serviceId, body.ApplicationId, body.ResourceName, perms,
                body.EnvVarPrefix, hook);
            var r = await m.Send(cmd, ct);
            return r.IsSuccess
                ? Results.Created($"/api/bindings/{r.Value.Id}", r.Value)
                : MapError(r.Error);
        }).WithName("CreateBinding");

        // Standalone bindings endpoints (acción independiente del service path).
        var bindings = app.MapGroup("/api/bindings").WithTags("Services").RequireAuthorization();

        bindings.MapDelete("/{bindingId}", async (string bindingId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new RevokeBindingCommand(bindingId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithName("RevokeBinding");

        bindings.MapPost("/{bindingId}/rotate", async (string bindingId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new RotateCredentialsCommand(bindingId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithName("RotateBindingCredentials");

        return app;
    }

    public sealed record CreateServiceRequest(string TemplateId, string Slug, string Name, string TargetVmId, bool? ExposedExternally);
    public sealed record CreateBindingRequest(
        string ApplicationId,
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
