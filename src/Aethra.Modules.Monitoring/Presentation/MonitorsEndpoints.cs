using Aethra.Modules.Monitoring.UseCases.Commands;
using Aethra.Modules.Monitoring.UseCases.Queries;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Aethra.Modules.Monitoring.Presentation;

public static class MonitorsEndpoints
{
    public static IEndpointRouteBuilder MapMonitorsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/monitors").WithTags("Monitoring").RequireAuthorization();

        group.MapGet("/", async (
            [FromQuery(Name = "instance_id")] string? instanceId,
            [FromQuery(Name = "project_id")] string? projectId,
            [FromQuery] string? status,
            [FromQuery(Name = "enabled")] bool? enabled,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new ListMonitorsQuery(instanceId, projectId, status, enabled), ct).ConfigureAwait(false);
            return ToResult(result);
        })
        .WithName("ListMonitors");

        group.MapGet("/overview", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetMonitorSummaryQuery(), ct).ConfigureAwait(false);
            return ToResult(result);
        })
        .WithName("GetMonitorOverview");

        group.MapGet("/{monitorId}", async (string monitorId, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetMonitorByIdQuery(monitorId), ct).ConfigureAwait(false);
            return ToResult(result);
        })
        .WithName("GetMonitorById");

        group.MapGet("/{monitorId}/checks", async (
            string monitorId,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new ListMonitorChecksQuery(monitorId, limit ?? 100), ct).ConfigureAwait(false);
            return ToResult(result);
        })
        .WithName("ListMonitorChecks");

        group.MapPost("/", async ([FromBody] CreateMonitorRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var cmd = new CreateMonitorCommand(
                Slug: body.Slug,
                Name: body.Name,
                Url: body.Url,
                HttpMethod: body.HttpMethod ?? "GET",
                ExpectedStatusCodes: body.ExpectedStatusCodes,
                IntervalSec: body.IntervalSec,
                TimeoutMs: body.TimeoutMs,
                Headers: body.Headers,
                BodyTemplate: body.BodyTemplate,
                InstanceId: body.InstanceId,
                ProjectId: body.ProjectId);
            var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
            return result.IsSuccess
                ? Results.Created($"/api/monitors/{result.Value.Id}", result.Value)
                : MapError(result.Error);
        })
        .WithName("CreateMonitor");

        group.MapPatch("/{monitorId}", async (
            string monitorId,
            [FromBody] UpdateMonitorRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new UpdateMonitorCommand(
                MonitorId: monitorId,
                Name: body.Name,
                Url: body.Url,
                HttpMethod: body.HttpMethod,
                ExpectedStatusCodes: body.ExpectedStatusCodes,
                IntervalSec: body.IntervalSec,
                TimeoutMs: body.TimeoutMs,
                Headers: body.Headers,
                ClearHeaders: body.ClearHeaders ?? false,
                BodyTemplate: body.BodyTemplate,
                ClearBodyTemplate: body.ClearBodyTemplate ?? false,
                InstanceId: body.InstanceId,
                ClearInstanceId: body.ClearInstanceId ?? false,
                ProjectId: body.ProjectId,
                ClearProjectId: body.ClearProjectId ?? false);
            var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
            return ToResult(result);
        })
        .WithName("UpdateMonitor");

        group.MapDelete("/{monitorId}", async (string monitorId, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new DeleteMonitorCommand(monitorId), ct).ConfigureAwait(false);
            return result.IsSuccess ? Results.NoContent() : MapError(result.Error);
        })
        .WithName("DeleteMonitor");

        group.MapPost("/{monitorId}/enable", async (string monitorId, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new EnableMonitorCommand(monitorId), ct).ConfigureAwait(false);
            return ToResult(result);
        })
        .WithName("EnableMonitor");

        group.MapPost("/{monitorId}/disable", async (string monitorId, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new DisableMonitorCommand(monitorId), ct).ConfigureAwait(false);
            return ToResult(result);
        })
        .WithName("DisableMonitor");

        group.MapPost("/{monitorId}/trigger", async (string monitorId, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new TriggerMonitorCheckCommand(monitorId), ct).ConfigureAwait(false);
            return ToResult(result);
        })
        .WithName("TriggerMonitorCheck");

        return app;
    }

    public sealed record CreateMonitorRequest(
        string Slug,
        string Name,
        string Url,
        string? HttpMethod,
        IReadOnlyList<int>? ExpectedStatusCodes,
        int? IntervalSec,
        int? TimeoutMs,
        IReadOnlyDictionary<string, string>? Headers,
        string? BodyTemplate,
        string? InstanceId,
        string? ProjectId);

    public sealed record UpdateMonitorRequest(
        string? Name,
        string? Url,
        string? HttpMethod,
        IReadOnlyList<int>? ExpectedStatusCodes,
        int? IntervalSec,
        int? TimeoutMs,
        IReadOnlyDictionary<string, string>? Headers,
        bool? ClearHeaders,
        string? BodyTemplate,
        bool? ClearBodyTemplate,
        string? InstanceId,
        bool? ClearInstanceId,
        string? ProjectId,
        bool? ClearProjectId);

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
