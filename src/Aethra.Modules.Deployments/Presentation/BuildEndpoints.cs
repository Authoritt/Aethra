using Aethra.Modules.Deployments.Domain.Build;
using Aethra.Modules.Deployments.UseCases.Build.Commands;
using Aethra.Modules.Deployments.UseCases.Build.Queries;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Aethra.Modules.Deployments.Presentation;

/// <summary>
/// Endpoints REST del pipeline de Build. Todos requieren autenticación — el webhook anónimo
/// vive en <see cref="WebhookEndpoints"/>.
/// </summary>
public static class BuildEndpoints
{
    // Builds son parte del pipeline de Deployments — reusamos su catálogo de scopes.
    // Trigger/Cancel exigen 'deployments:trigger' (la acción que afecta workloads); las
    // listas/logs sólo 'deployments:read'.
    private const string ScopeRead = "scope:deployments:read";
    private const string ScopeTrigger = "scope:deployments:trigger";

    public static IEndpointRouteBuilder MapBuildEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/builds").WithTags("Deployments");

        group.MapGet("/templates/{templateId}", async (
            string templateId,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
            ToResult(await mediator.Send(new ListBuildsQuery(templateId, limit ?? 50), ct).ConfigureAwait(false)))
            .RequireAuthorization(ScopeRead)
            .WithName("ListBuilds");

        group.MapGet("/{buildId}", async (
            string buildId,
            IMediator mediator,
            CancellationToken ct) =>
            ToResult(await mediator.Send(new GetBuildByIdQuery(buildId), ct).ConfigureAwait(false)))
            .RequireAuthorization(ScopeRead)
            .WithName("GetBuild");

        group.MapGet("/{buildId}/logs", async (
            string buildId,
            [FromQuery] long? since,
            IMediator mediator,
            CancellationToken ct) =>
            ToResult(await mediator.Send(new GetBuildLogsQuery(buildId, since ?? 0), ct).ConfigureAwait(false)))
            .RequireAuthorization(ScopeRead)
            .WithName("GetBuildLogs");

        group.MapPost("/templates/{templateId}/trigger", async (
            string templateId,
            [FromBody] TriggerBuildRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new TriggerBuildCommand(
                TemplateId: templateId,
                GitSha: body.GitSha ?? string.Empty,
                GitRef: body.GitRef ?? "refs/heads/main",
                Trigger: BuildTrigger.Manual,
                TriggeredBy: body.TriggeredBy);
            var r = await mediator.Send(cmd, ct).ConfigureAwait(false);
            return r.IsSuccess
                ? Results.Created($"/api/builds/{r.Value.Id}", r.Value)
                : MapError(r.Error);
        })
        .RequireAuthorization(ScopeTrigger)
        .WithName("TriggerBuild");

        group.MapPost("/{buildId}/cancel", async (
            string buildId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var r = await mediator.Send(new CancelBuildCommand(buildId), ct).ConfigureAwait(false);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeTrigger)
        .WithName("CancelBuild");

        return app;
    }

    /// <summary>
    /// Payload del trigger manual. <c>GitSha</c> es obligatorio porque la spec del Build
    /// exige conocer el commit exacto (no "el HEAD del branch"); la UI debería resolverlo
    /// contra GitHub antes de invocar este endpoint.
    /// </summary>
    public sealed record TriggerBuildRequest(string? GitSha, string? GitRef, string? TriggeredBy);

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
