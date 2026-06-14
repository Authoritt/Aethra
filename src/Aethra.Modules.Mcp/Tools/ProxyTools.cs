using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Proxy.UseCases.Routes.Queries;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

/// <summary>
/// Herramientas MCP de solo-lectura sobre el reverse-proxy YARP (rutas hostname→contenedor).
/// </summary>
[McpServerToolType]
public sealed class ProxyTools(IMediator mediator, IMcpCallerContext caller)
{
    [McpServerTool(Name = "aethra_list_routes", ReadOnly = true, OpenWorld = false)]
    [Description("Lista las rutas del reverse-proxy YARP: hostname, path prefix, backend (contenedor destino), si "
        + "tiene TLS y el estado/vencimiento del certificado. Read-only; útil para depurar routing/ingress. "
        + "No expone el certificado ni claves privadas (sólo el estado).")]
    public async Task<object> ListRoutesAsync(CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProxyRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ProxyRead);
        }
        var result = await mediator.Send(new ListRoutesQuery(), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }
}
