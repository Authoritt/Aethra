using Aethra.Modules.Deployments.Domain;
using Aethra.Modules.Deployments.UseCases.Commands;
using Aethra.Modules.Deployments.UseCases.Queries;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Aethra.Modules.Deployments.Presentation;

public static class DeploysEndpoints
{
    public static IEndpointRouteBuilder MapDeploysEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/deploys").WithTags("Deployments").RequireAuthorization();

        group.MapGet("/applications/{appId}", async (string appId, [FromQuery] int? limit, IMediator m, CancellationToken ct) =>
            ToResult(await m.Send(new ListDeploysQuery(appId, limit ?? 50), ct)))
            .WithName("ListDeploys");

        group.MapGet("/{jobId}", async (string jobId, IMediator m, CancellationToken ct) =>
            ToResult(await m.Send(new GetDeployByIdQuery(jobId), ct)))
            .WithName("GetDeploy");

        group.MapGet("/{jobId}/logs", async (string jobId, [FromQuery] long? since, IMediator m, CancellationToken ct) =>
            ToResult(await m.Send(new GetDeployLogsQuery(jobId, since ?? 0), ct)))
            .WithName("GetDeployLogs");

        group.MapPost("/applications/{appId}/trigger", async (
            string appId,
            [FromBody] TriggerDeployRequest body,
            IMediator m,
            CancellationToken ct) =>
        {
            var cmd = new TriggerDeployCommand(
                ApplicationId: appId,
                GitSha: body.GitSha,
                Branch: body.Branch ?? "main",
                Trigger: DeployTrigger.Manual,
                TriggeredBy: body.TriggeredBy);
            var r = await m.Send(cmd, ct);
            return r.IsSuccess
                ? Results.Created($"/api/deploys/{r.Value.JobId}", r.Value)
                : MapError(r.Error);
        })
        .WithName("TriggerDeploy");

        return app;
    }

    public sealed record TriggerDeployRequest(string? GitSha, string? Branch, string? TriggeredBy);

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
