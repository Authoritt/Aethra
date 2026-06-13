using System.ComponentModel;
using Aethra.Modules.Cloudflare.UseCases.DnsRecords.Commands;
using Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands;
using Aethra.Modules.Cloudflare.UseCases.Tunnels.Queries;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Mcp.UseCases;
using Aethra.Shared.Contracts.Cloudflare;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

[McpServerToolType]
public sealed class CloudflareTools(IMediator mediator, IMcpCallerContext caller)
{
    [McpServerTool(Name = "aethra_create_dns_record", Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Crea un DNS record en Cloudflare (y persiste la copia local con su id externo). Tipos: A, AAAA, "
        + "CNAME, TXT, MX. Para apuntar un hostname a una Instance preferí aethra_attach_domain (hace DNS+ruta+monitor); "
        + "usá esta tool para records sueltos. Devuelve el record creado.")]
    public async Task<object> CreateDnsRecordAsync(
        [Description("ID de la zona Cloudflare gestionada en Aethra.")] string zoneId,
        [Description("Tipo: A | AAAA | CNAME | TXT | MX.")] string type,
        [Description("Nombre del record (FQDN, ej. 'api.midominio.com').")] string name,
        [Description("Contenido: IP (A/AAAA), target (CNAME), valor (TXT), host (MX).")] string content,
        [Description("TTL en segundos (1 = automático).")] int ttl,
        [Description("Si true, proxea por Cloudflare (sólo válido para A/AAAA/CNAME).")] bool proxied,
        [Description("Comentario opcional.")] string? comment,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.CloudflareWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.CloudflareWrite);
        }
        var result = await mediator
            .Send(new CreateDnsRecordCommand(zoneId, type, name, content, ttl, proxied, comment), ct)
            .ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_delete_dns_record", Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Borra un DNS record de Cloudflare (y la copia local en Aethra). Usá dry_run=true primero para confirmar.")]
    public async Task<object> DeleteDnsRecordAsync(
        [Description("ID del DNS record en Aethra (el que devuelve aethra_create_dns_record).")] string recordId,
        [Description("Si true, NO borra — devuelve el plan.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.CloudflareWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.CloudflareWrite);
        }
        if (dryRun)
        {
            return McpResponses.DryRun(
                wouldCall: $"delete dns record {recordId}",
                plan: new { recordId, action = "delete DNS record on Cloudflare + local copy" });
        }
        var result = await mediator.Send(new DeleteDnsRecordCommand(recordId), ct).ConfigureAwait(false);
        return result.IsSuccess
            ? McpResponses.Ok(new { record_id = recordId, deleted = true })
            : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_attach_domain", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Adjunta un hostname a una Instance: crea DNS record en Cloudflare (CNAME proxied), crea Route YARP y opcionalmente un Monitor HTTP. Cada paso devuelve su propio status.")]
    public async Task<object> AttachDomainAsync(
        [Description("ID de la Instance (formato 'ins_...').")] string instanceId,
        [Description("Hostname público (FQDN).")] string hostname,
        [Description("ID interno de la zona Cloudflare (formato 'cfz_...'). Si null, se intenta resolver por sufijo del hostname.")] string? cloudflareZoneId,
        [Description("Si true, crea también un monitor HTTP para el hostname.")] bool createMonitor,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.CloudflareWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.CloudflareWrite);
        }
        var cmd = new AttachDomainCommand(instanceId, hostname, cloudflareZoneId, createMonitor);
        var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    // ---------------------------------------------------------------------
    // F13.12 — Túnel gestionado remoto (paridad MCP con /api/cloudflare/tunnel/*).
    // ---------------------------------------------------------------------

    [McpServerTool(Name = "aethra_get_tunnel", ReadOnly = true, OpenWorld = false)]
    [Description("Devuelve el túnel CF gestionado (sin token) + su config de ingress remota actual, o null si no hay ninguno.")]
    public async Task<object> GetTunnelAsync(CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.CloudflareWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.CloudflareWrite);
        }
        var r = await mediator.Send(new GetTunnelQuery(), ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(new { tunnel = r.Value }) : McpResponses.FromError(r.Error);
    }

    [McpServerTool(Name = "aethra_register_tunnel", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Registra (o actualiza) el Cloudflare Tunnel gestionado: guarda el API token cifrado y la VM del connector. Verifica el token contra el API real.")]
    public async Task<object> RegisterTunnelAsync(
        [Description("Account ID de Cloudflare (hex 32).")] string accountId,
        [Description("UUID del túnel.")] string tunnelId,
        [Description("Nombre del túnel (ej. 'authorit-apps').")] string name,
        [Description("API token con scope Cloudflare Tunnel:Edit.")] string apiToken,
        [Description("VM (vm_...) donde corre el connector (la que tiene los servicios en localhost).")] string? targetVmId,
        [Description("Servicio Aethra (default http://localhost:5080).")] string? aethraService,
        [Description("Servicio catch-all (default https://localhost:443).")] string? fallbackService,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.CloudflareWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.CloudflareWrite);
        }
        var r = await mediator.Send(new RegisterTunnelCommand(
            accountId, tunnelId, name, apiToken, aethraService, fallbackService, true, targetVmId), ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(new { tunnel = r.Value }) : McpResponses.FromError(r.Error);
    }

    [McpServerTool(Name = "aethra_promote_tunnel_remote", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Promueve la config del túnel a gestión remota (source=cloudflare) re-publicando el ingress actual. Paso previo al connector con token.")]
    public async Task<object> PromoteTunnelRemoteAsync(CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.CloudflareWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.CloudflareWrite);
        }
        var r = await mediator.Send(new PromoteTunnelRemoteCommand(), ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(new { rules = r.Value, source = "cloudflare" }) : McpResponses.FromError(r.Error);
    }

    [McpServerTool(Name = "aethra_ensure_tunnel_hostname", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Asegura una regla de ingress para el hostname → servicio de Aethra, sin reiniciar el túnel (cero blip). Idempotente.")]
    public async Task<object> EnsureTunnelHostnameAsync(
        [Description("Hostname (FQDN) a enrutar al proxy de Aethra.")] string hostname,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.CloudflareWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.CloudflareWrite);
        }
        var r = await mediator.Send(new EnsureTunnelHostnameCommand(hostname), ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(new { hostname, status = "ensured" }) : McpResponses.FromError(r.Error);
    }

    [McpServerTool(Name = "aethra_remove_tunnel_hostname", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Quita la(s) regla(s) de ingress de un hostname del túnel (sin reiniciar).")]
    public async Task<object> RemoveTunnelHostnameAsync(
        [Description("Hostname (FQDN) a quitar.")] string hostname,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.CloudflareWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.CloudflareWrite);
        }
        var r = await mediator.Send(new RemoveTunnelHostnameCommand(hostname), ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(new { hostname, status = "removed" }) : McpResponses.FromError(r.Error);
    }

    [McpServerTool(Name = "aethra_deploy_tunnel_connector", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Despliega el connector cloudflared como contenedor gestionado en la VM del túnel (flip a remoto, cero SSH). Corre en background.")]
    public async Task<object> DeployTunnelConnectorAsync(
        [Description("VM (vm_...) destino. Si null, usa la TargetVmId del túnel.")] string? vmId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.CloudflareWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.CloudflareWrite);
        }
        await mediator.Publish(new TunnelConnectorDeployRequestedIntegrationEvent(vmId, "mcp"), ct).ConfigureAwait(false);
        return McpResponses.Ok(new { status = "queued", note = "Connector desplegándose en background." });
    }
}
