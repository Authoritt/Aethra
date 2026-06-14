using Aethra.Modules.Metrics.UseCases.Queries;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Aethra.Modules.Metrics.Presentation;

public static class MetricsEndpoints
{
    private const string ScopeRead = "scope:metrics:read";

    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/metrics").WithTags("Metrics");

        group.MapGet("/vms/{vmId}/latest", async (string vmId, int? limit, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetLatestMetricsQuery(vmId, limit ?? 60), ct);
            return ToResult(result);
        })
        .RequireAuthorization(ScopeRead)
        .WithName("GetLatestVmMetrics")
        .WithDescription("Últimas N muestras de métricas de una VM (cronológico ascendente).");

        group.MapGet("/vms/{vmId}/history", async (string vmId, int? hours, int? points, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetMetricsHistoryQuery(vmId, hours ?? 24, points ?? 240), ct);
            return ToResult(result);
        })
        .RequireAuthorization(ScopeRead)
        .WithName("GetVmMetricsHistory")
        .WithDescription("Historial de métricas de una VM en una ventana (hours, default 24, máx 168) "
            + "downsampled a 'points' puntos (default 240). Para rangos largos sin miles de muestras.");

        return app;
    }

    private static IResult ToResult<T>(Result<T> r)
        => r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(r.Error.Message);
}
