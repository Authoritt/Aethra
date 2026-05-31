using Aethra.Modules.Vms.UseCases.Vms.Commands;
using Aethra.Modules.Vms.UseCases.Vms.Queries;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Aethra.Modules.Vms.Presentation;

public static class VmsEndpoints
{
    public static IEndpointRouteBuilder MapVmsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vms").WithTags("Vms").RequireAuthorization();

        group.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
            ToResult(await mediator.Send(new ListVmsQuery(), ct)))
            .WithName("ListVms");

        group.MapGet("/{vmId}", async (string vmId, IMediator mediator, CancellationToken ct) =>
            ToResult(await mediator.Send(new GetVmByIdQuery(vmId), ct)))
            .WithName("GetVm");

        group.MapPost("/", async ([FromBody] RegisterVmRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var cmd = new RegisterVmCommand(body.Name, body.Slug, body.PublicIp, body.PrivateIp, body.Description);
            var r = await mediator.Send(cmd, ct);
            return r.IsSuccess ? Results.Created($"/api/vms/{r.Value.VmId}", r.Value) : MapError(r.Error);
        })
        .WithName("RegisterVm")
        .WithDescription("Registra una VM. La respuesta incluye el TOKEN UNA SOLA VEZ.");

        return app;
    }

    public sealed record RegisterVmRequest(string Name, string? Slug, string? PublicIp, string? PrivateIp,
        string? Description);

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
