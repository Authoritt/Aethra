using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Proxy.UseCases.Certificates.Queries;
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

    [McpServerTool(Name = "aethra_list_certificates", ReadOnly = true, OpenWorld = false)]
    [Description("Lista los certificados TLS gestionados por el proxy: hostname, estado "
        + "(none/pending/issued/failed/renewing), fechas (issued/notBefore/notAfter/renewAfter) y último error. "
        + "Read-only; útil para ver vencimientos/estado de TLS. NUNCA expone el PEM ni la clave privada.")]
    public async Task<object> ListCertificatesAsync(CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProxyRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ProxyRead);
        }
        var result = await mediator.Send(new ListCertificatesQuery(), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }
}
