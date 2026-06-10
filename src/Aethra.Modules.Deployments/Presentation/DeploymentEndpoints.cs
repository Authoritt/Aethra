using Aethra.Modules.Deployments.UseCases.Deployment.Commands;
using Aethra.Modules.Deployments.UseCases.Deployment.Queries;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Aethra.Modules.Deployments.Presentation;

/// <summary>
/// Endpoints REST del pipeline de Deployment. Todos requieren autenticación — el flujo
/// automático del fan-out no pasa por HTTP, vive dentro del handler MediatR cross-module
/// <c>BuildCompletedHandler</c>.
/// </summary>
public static class DeploymentEndpoints
{
    // Lecturas → 'deployments:read'. Mutaciones (trigger/cancel/promote) → 'deployments:trigger'.
    // 'deployments:write' queda reservado para CRUD futuro de la entidad Deployment (no usado hoy).
    private const string ScopeRead = "scope:deployments:read";
    private const string ScopeTrigger = "scope:deployments:trigger";

    public static IEndpointRouteBuilder MapDeploymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/deployments")
            .WithTags("Deployments");

        group.MapGet("/instances/{instanceId}", async (
            string instanceId,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
            ToResult(await mediator.Send(new ListDeploymentsQuery(instanceId, limit ?? 50), ct)
                .ConfigureAwait(false)))
            .RequireAuthorization(ScopeRead)
            .WithName("ListDeployments");

        group.MapGet("/{deploymentId}", async (
            string deploymentId,
            IMediator mediator,
            CancellationToken ct) =>
            ToResult(await mediator.Send(new GetDeploymentByIdQuery(deploymentId), ct)
                .ConfigureAwait(false)))
            .RequireAuthorization(ScopeRead)
            .WithName("GetDeployment");

        group.MapGet("/{deploymentId}/logs", async (
            string deploymentId,
            [FromQuery] long? since,
            IMediator mediator,
            CancellationToken ct) =>
            ToResult(await mediator.Send(new GetDeploymentLogsQuery(deploymentId, since ?? 0), ct)
                .ConfigureAwait(false)))
            .RequireAuthorization(ScopeRead)
            .WithName("GetDeploymentLogs");

        group.MapPost("/builds/{buildId}/instances/{instanceId}/trigger", async (
            string buildId,
            string instanceId,
            [FromBody] TriggerDeploymentRequest? body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new TriggerDeploymentCommand(
                BuildId: buildId,
                InstanceId: instanceId,
                TriggeredBy: body?.TriggeredBy);
            var r = await mediator.Send(cmd, ct).ConfigureAwait(false);
            return r.IsSuccess
                ? Results.Created($"/api/deployments/{r.Value.Id}", r.Value)
                : MapError(r.Error);
        })
        .RequireAuthorization(ScopeTrigger)
        .WithName("TriggerDeployment");

        group.MapPost("/{deploymentId}/cancel", async (
            string deploymentId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var r = await mediator.Send(new CancelDeploymentCommand(deploymentId), ct)
                .ConfigureAwait(false);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeTrigger)
        .WithName("CancelDeployment");

        group.MapPost("/{deploymentId}/rollback", async (
            string deploymentId,
            [FromBody] RollbackDeploymentRequest? body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new RollbackDeploymentCommand(
                SourceDeploymentId: deploymentId,
                TriggeredBy: body?.TriggeredBy);
            var r = await mediator.Send(cmd, ct).ConfigureAwait(false);
            return r.IsSuccess
                ? Results.Created($"/api/deployments/{r.Value.Id}", r.Value)
                : MapError(r.Error);
        })
        .RequireAuthorization(ScopeTrigger)
        .WithName("RollbackDeployment");

        group.MapPost("/{deploymentId}/promote/{toInstanceId}", async (
            string deploymentId,
            string toInstanceId,
            [FromBody] PromoteDeploymentRequest? body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new PromoteDeploymentCommand(
                SourceDeploymentId: deploymentId,
                TargetInstanceId: toInstanceId,
                TriggeredBy: body?.TriggeredBy);
            var r = await mediator.Send(cmd, ct).ConfigureAwait(false);
            return r.IsSuccess
                ? Results.Created($"/api/deployments/{r.Value.Id}", r.Value)
                : MapError(r.Error);
        })
        .RequireAuthorization(ScopeTrigger)
        .WithName("PromoteDeployment");

        return app;
    }

    /// <summary>
    /// Payload del trigger manual. <c>TriggeredBy</c> es informativo: la UI puede pasar el
    /// nombre del operador para auditoría; nunca se usa para autorización (eso ya lo cubre
    /// el cookie/auth).
    /// </summary>
    public sealed record TriggerDeploymentRequest(string? TriggeredBy);

    /// <summary>
    /// Payload del promote. <c>TriggeredBy</c> es informativo igual que en el trigger manual.
    /// </summary>
    public sealed record PromoteDeploymentRequest(string? TriggeredBy);

    public sealed record RollbackDeploymentRequest(string? TriggeredBy);

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
