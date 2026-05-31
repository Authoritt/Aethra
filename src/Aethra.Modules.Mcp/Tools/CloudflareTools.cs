using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Mcp.UseCases;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

[McpServerToolType]
public sealed class CloudflareTools(IMediator mediator, IMcpCallerContext caller)
{
    [McpServerTool(Name = "aethra_attach_domain", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Adjunta un hostname a una Application: crea DNS record en Cloudflare (CNAME proxied), crea Route YARP y opcionalmente un Monitor HTTP. Cada paso devuelve su propio status.")]
    public async Task<object> AttachDomainAsync(
        [Description("ID de la Application (formato 'app_...').")] string applicationId,
        [Description("Hostname público (FQDN).")] string hostname,
        [Description("ID interno de la zona Cloudflare (formato 'cfz_...'). Si null, se intenta resolver por sufijo del hostname.")] string? cloudflareZoneId,
        [Description("Si true, crea también un monitor HTTP para el hostname.")] bool createMonitor,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.CloudflareWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.CloudflareWrite);
        }
        var cmd = new AttachDomainCommand(applicationId, hostname, cloudflareZoneId, createMonitor);
        var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }
}
