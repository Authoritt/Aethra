using Aethra.Modules.Proxy.UseCases.Routes.Commands;
using Aethra.Modules.Proxy.UseCases.Routes.Queries;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Aethra.Modules.Proxy.Presentation;

public static class RoutesEndpoints
{
    private const string ScopeRead = "scope:proxy:read";
    private const string ScopeWrite = "scope:proxy:write";

    public static IEndpointRouteBuilder MapRoutesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/proxy/routes").WithTags("Proxy");

        group.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
            ToResult(await mediator.Send(new ListRoutesQuery(), ct)))
            .RequireAuthorization(ScopeRead)
            .WithName("ListRoutes");

        group.MapPost("/", async ([FromBody] CreateRouteRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var cmd = new CreateRouteCommand(
                body.Hostname,
                body.BackendUrl,
                body.TlsEnabled,
                body.PathPrefix,
                body.OperationalOwnerType,
                body.OperationalOwnerId,
                body.Origin ?? "manual");
            var r = await mediator.Send(cmd, ct);
            return r.IsSuccess ? Results.Created($"/api/proxy/routes/{r.Value.Id}", r.Value) : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("CreateRoute");

        group.MapPatch("/{routeId}", async (string routeId, [FromBody] UpdateRouteRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(new UpdateRouteCommand(routeId, body.BackendUrl, body.TlsEnabled), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("UpdateRoute");

        group.MapDelete("/{routeId}", async (string routeId, IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(new DeleteRouteCommand(routeId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("DeleteRoute");

        return app;
    }

    public sealed record CreateRouteRequest(
        string Hostname,
        string BackendUrl,
        bool TlsEnabled,
        string? PathPrefix = null,
        string? OperationalOwnerType = null,
        string? OperationalOwnerId = null,
        string? Origin = null);

    public sealed record UpdateRouteRequest(string BackendUrl, bool TlsEnabled);

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
