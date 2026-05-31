using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Metrics.UseCases.Queries;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

[McpServerToolType]
public sealed class MetricsTools(IMediator mediator, IMcpCallerContext caller)
{
    [McpServerTool(Name = "aethra_query_metrics", ReadOnly = true, OpenWorld = false)]
    [Description("Devuelve las últimas N muestras de métricas de VM (CPU, memoria, red). Range/resolution se reservan para F6.5 — por ahora limit y vm_id.")]
    public async Task<object> QueryAsync(
        [Description("ID de la VM. Obligatorio en F6.")] string vmId,
        [Description("Cantidad máxima de puntos (1-1000, default 60).")] int limit,
        [Description("Container name (futuro — F6.5). Ignorado en esta versión.")] string? container,
        [Description("Rango (futuro: ej '1h', '24h'). Ignorado en F6.")] string? range,
        [Description("Resolución (futuro: ej '15s', '1m'). Ignorada en F6.")] string? resolution,
        CancellationToken ct)
    {
        _ = container; _ = range; _ = resolution;
        if (!caller.HasScope(McpScopes.MetricsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.MetricsRead);
        }
        if (string.IsNullOrWhiteSpace(vmId))
        {
            return McpResponses.Failure("metrics.vm_id_required",
                "vm_id es obligatorio. Para métricas de contenedor (filtro 'container'), espera F6.5.",
                "validation");
        }
        var effective = limit <= 0 ? 60 : limit;
        var result = await mediator.Send(new GetLatestMetricsQuery(vmId, effective), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }
}
