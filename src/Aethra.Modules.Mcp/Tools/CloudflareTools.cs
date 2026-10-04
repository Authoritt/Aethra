using System.ComponentModel;
using Aethra.Modules.Cloudflare.Application.Dtos;
using Aethra.Modules.Cloudflare.Domain;
using Aethra.Modules.Cloudflare.UseCases.DnsRecords.Commands;
using Aethra.Modules.Cloudflare.UseCases.DnsRecords.Queries;
using Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands;
using Aethra.Modules.Cloudflare.UseCases.Tunnels.Queries;
using Aethra.Modules.Cloudflare.UseCases.Zones.Queries;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Mcp.UseCases;
using Aethra.Shared.Contracts.Cloudflare;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using MediatR;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

[McpServerToolType]
public sealed class CloudflareTools(IMediator mediator, IMcpCallerContext caller, IConfiguration configuration)
{
    [McpServerTool(Name = "aethra_create_dns_record", Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Crea un DNS record en Cloudflare (y persiste la copia local con su id externo). Tipos: A, AAAA, "
        + "CNAME, TXT, MX. Para apuntar un hostname a una Instance preferí aethra_attach_domain (hace DNS+ruta+monitor); "
        + "usá esta tool para records sueltos. Devuelve el record creado."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
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

    [McpServerTool(Name = "aethra_update_dns_record", Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Actualiza (patch) un DNS record en Cloudflare: content, ttl, proxied y/o comment. El tipo y el "
        + "nombre NO se cambian (borrá y recreá para eso). Sólo aplica los campos provistos. Devuelve el record actualizado."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
    public async Task<object> UpdateDnsRecordAsync(
        [Description("ID del DNS record en Aethra.")] string recordId,
        [Description("Nuevo contenido (IP/target/valor). Omitir/null = no cambiar.")] string? content,
        [Description("Nuevo TTL en segundos (1 = automático). Omitir/null = no cambiar.")] int? ttl,
        [Description("Proxy Cloudflare on/off (sólo A/AAAA/CNAME). Omitir/null = no cambiar.")] bool? proxied,
        [Description("Nuevo comentario. Omitir/null = no cambiar.")] string? comment,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.CloudflareWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.CloudflareWrite);
        }
        var result = await mediator
            .Send(new UpdateDnsRecordCommand(recordId, content, ttl, proxied, comment), ct)
            .ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_list_dns_records", ReadOnly = true, OpenWorld = false)]
    [Description("Lista los DNS records que Aethra gestiona en una zona Cloudflare: id, tipo, nombre, contenido, "
        + "ttl, proxied, comentario y estado de sync. Read-only. Útil para descubrir el id antes de "
        + "aethra_update_dns_record / aethra_delete_dns_record.")]
    public async Task<object> ListDnsRecordsAsync(
        [Description("ID de la zona Cloudflare gestionada en Aethra.")] string zoneId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.CloudflareRead))
        {
            return McpResponses.InsufficientScope(McpScopes.CloudflareRead);
        }
        var result = await mediator.Send(new ListDnsRecordsQuery(zoneId), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_attach_domain", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Adjunta un hostname a una Instance: crea DNS record en Cloudflare (CNAME proxied), crea Route YARP y opcionalmente un Monitor HTTP. Cada paso devuelve su propio status."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
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
    public async Task<object> GetTunnelAsync(
        [Description("UUID del túnel. Null = túnel CentralProxy histórico.")] string? tunnelId,
        [Description("Modo opcional: central_proxy | direct.")] string? mode,
        [Description("VM destino para resolver un túnel Direct cuando no se pasa tunnelId.")] string? targetVmId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.CloudflareWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.CloudflareWrite);
        }
        var r = await ResolveTunnelAsync(tunnelId, mode, targetVmId, ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(new { tunnel = r.Value }) : McpResponses.FromError(r.Error);
    }

    [McpServerTool(Name = "aethra_register_tunnel", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Registra (o actualiza) el Cloudflare Tunnel gestionado: guarda el API token cifrado y la VM del connector. Verifica el token contra el API real."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
    public async Task<object> RegisterTunnelAsync(
        [Description("Account ID de Cloudflare (hex 32).")] string accountId,
        [Description("UUID del túnel.")] string tunnelId,
        [Description("Nombre del túnel (ej. 'authorit-apps').")] string name,
        [Description("API token con scope Cloudflare Tunnel:Edit.")] string apiToken,
        [Description("VM (vm_...) donde corre el connector (la que tiene los servicios en localhost).")] string? targetVmId,
        [Description("Servicio Aethra (default http://localhost:5080).")] string? aethraService,
        [Description("Servicio catch-all (default https://localhost:443).")] string? fallbackService,
        [Description("Modo opcional: central_proxy | direct. Null = central_proxy.")] string? mode,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.CloudflareWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.CloudflareWrite);
        }
        var parsedMode = ParseTunnelMode(mode);
        if (parsedMode.IsFailure)
        {
            return McpResponses.FromError(parsedMode.Error);
        }

        var r = await mediator.Send(new RegisterTunnelCommand(
            accountId, tunnelId, name, apiToken, aethraService, fallbackService, true, targetVmId,
            parsedMode.Value), ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(new { tunnel = r.Value }) : McpResponses.FromError(r.Error);
    }

    [McpServerTool(Name = "aethra_promote_tunnel_remote", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Promueve la config del túnel a gestión remota (source=cloudflare) re-publicando el ingress actual. Paso previo al connector con token."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
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
    [Description("Asegura una regla de ingress para el hostname → servicio de Aethra, sin reiniciar el túnel (cero blip). Idempotente."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
    public async Task<object> EnsureTunnelHostnameAsync(
        [Description("Hostname (FQDN) a enrutar al proxy de Aethra.")] string hostname,
        [Description("UUID del túnel. Null = túnel CentralProxy histórico.")] string? tunnelId,
        [Description("Modo opcional: central_proxy | direct.")] string? mode,
        [Description("Servicio de ingress opcional. Null = AethraService del túnel.")] string? service,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.CloudflareWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.CloudflareWrite);
        }
        var normalizedHost = hostname.Trim();
        var dnsTarget = await ResolveTunnelDnsTargetAsync(tunnelId, mode, ct).ConfigureAwait(false);
        if (dnsTarget.IsFailure)
        {
            return McpResponses.FromError(dnsTarget.Error);
        }

        var r = await mediator.Send(new EnsureTunnelHostnameCommand(normalizedHost, tunnelId, service), ct).ConfigureAwait(false);
        if (r.IsFailure)
        {
            return McpResponses.FromError(r.Error);
        }

        var zones = await mediator.Send(new ListZonesQuery(), ct).ConfigureAwait(false);
        if (zones.IsFailure)
        {
            return McpResponses.FromError(zones.Error);
        }
        var zone = zones.Value
            .Where(z => normalizedHost.Equals(z.Name, StringComparison.OrdinalIgnoreCase)
                || normalizedHost.EndsWith("." + z.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(z => z.Name.Length)
            .FirstOrDefault();
        if (zone is null)
        {
            return McpResponses.FromError(Error.NotFound(
                "cloudflare.zone_not_found_for_hostname",
                $"No hay una zona Cloudflare registrada que cubra '{normalizedHost}'."));
        }

        var records = await mediator.Send(new ListDnsRecordsQuery(zone.Id), ct).ConfigureAwait(false);
        if (records.IsFailure)
        {
            return McpResponses.FromError(records.Error);
        }
        var existing = records.Value
            .FirstOrDefault(x => string.Equals(x.Name, normalizedHost, StringComparison.OrdinalIgnoreCase));
        var tunnelCname = dnsTarget.Value.Target;
        if (dnsTarget.Value.Strict)
        {
            if (existing is not null && !ExistingCnameMatches(existing.Type, existing.Content, tunnelCname))
            {
                return McpResponses.FromError(Error.Conflict(
                    "cloudflare.hostname_record_conflict",
                    $"El record existente para '{normalizedHost}' no apunta al túnel esperado '{NormalizeDnsTarget(tunnelCname)}'."));
            }
        }
        else if (existing is not null
            && !string.Equals(existing.Type, "CNAME", StringComparison.OrdinalIgnoreCase))
        {
            return McpResponses.FromError(Error.Conflict(
                "cloudflare.hostname_record_conflict",
                $"Ya existe un record {existing.Type} para '{normalizedHost}'."));
        }

        string dnsStatus;
        if (existing is null)
        {
            var created = await mediator.Send(new CreateDnsRecordCommand(
                zone.Id, "CNAME", normalizedHost, tunnelCname, 1, true,
                "aethra tunnel ensure-hostname"), ct).ConfigureAwait(false);
            if (created.IsFailure)
            {
                return McpResponses.FromError(created.Error);
            }
            dnsStatus = "created";
        }
        else if (!string.Equals(existing.Content, tunnelCname, StringComparison.OrdinalIgnoreCase)
            || existing.Ttl != 1
            || !existing.Proxied)
        {
            var updated = await mediator.Send(new UpdateDnsRecordCommand(
                existing.Id, tunnelCname, 1, true, existing.Comment), ct).ConfigureAwait(false);
            if (updated.IsFailure)
            {
                return McpResponses.FromError(updated.Error);
            }
            dnsStatus = "updated";
        }
        else
        {
            dnsStatus = "already_configured";
        }

        return McpResponses.Ok(new
        {
            hostname = normalizedHost,
            ingress = "ensured",
            dns = dnsStatus,
            target = tunnelCname,
        });
    }

    [McpServerTool(Name = "aethra_remove_tunnel_hostname", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Quita la(s) regla(s) de ingress de un hostname del túnel (sin reiniciar)."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
    public async Task<object> RemoveTunnelHostnameAsync(
        [Description("Hostname (FQDN) a quitar.")] string hostname,
        [Description("UUID del túnel. Null = túnel CentralProxy histórico.")] string? tunnelId,
        [Description("Modo opcional: central_proxy | direct.")] string? mode,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.CloudflareWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.CloudflareWrite);
        }
        var parsedMode = ParseTunnelMode(mode);
        if (parsedMode.IsFailure)
        {
            return McpResponses.FromError(parsedMode.Error);
        }
        if (parsedMode.Value == TunnelMode.Direct && string.IsNullOrWhiteSpace(tunnelId))
        {
            return McpResponses.FromError(Error.Validation(
                "cloudflare.direct_tunnel_id_required",
                "tunnelId es obligatorio para quitar hostnames de un túnel Direct."));
        }

        var r = await mediator.Send(new RemoveTunnelHostnameCommand(hostname, tunnelId), ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(new { hostname, status = "removed" }) : McpResponses.FromError(r.Error);
    }

    [McpServerTool(Name = "aethra_deploy_tunnel_connector", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Despliega el connector cloudflared como contenedor gestionado en la VM del túnel (flip a remoto, cero SSH). Corre en background."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
    public async Task<object> DeployTunnelConnectorAsync(
        [Description("VM (vm_...) destino. Si null, usa la TargetVmId del túnel.")] string? vmId,
        [Description("UUID del túnel. Opcional; se usa para resolver su TargetVmId si es Direct.")] string? tunnelId,
        [Description("Modo opcional: central_proxy | direct.")] string? mode,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.CloudflareWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.CloudflareWrite);
        }
        var resolvedVmId = vmId;
        if (!string.IsNullOrWhiteSpace(tunnelId) || IsDirectMode(mode))
        {
            var tunnel = await ResolveTunnelAsync(tunnelId, mode, vmId, ct).ConfigureAwait(false);
            if (tunnel.IsFailure)
            {
                return McpResponses.FromError(tunnel.Error);
            }
            resolvedVmId = tunnel.Value?.TargetVmId ?? resolvedVmId;
        }

        await mediator.Publish(new TunnelConnectorDeployRequestedIntegrationEvent(resolvedVmId, "mcp"), ct).ConfigureAwait(false);
        return McpResponses.Ok(new { status = "queued", vmId = resolvedVmId, note = "Connector desplegándose en background." });
    }

    private async Task<Result<CloudflareTunnelDto?>> ResolveTunnelAsync(
        string? tunnelId,
        string? mode,
        string? targetVmId,
        CancellationToken ct)
    {
        var parsedMode = ParseTunnelMode(mode);
        if (parsedMode.IsFailure)
        {
            return parsedMode.Error;
        }

        if (parsedMode.Value == TunnelMode.Direct && string.IsNullOrWhiteSpace(tunnelId))
        {
            if (string.IsNullOrWhiteSpace(targetVmId))
            {
                return Error.Validation(
                    "cloudflare.direct_selector_required",
                    "mode=direct requiere tunnelId o targetVmId.");
            }

            return await mediator.Send(new GetTunnelForVmQuery(targetVmId.Trim()), ct).ConfigureAwait(false);
        }

        var r = await mediator.Send(new GetTunnelQuery(tunnelId), ct).ConfigureAwait(false);
        if (r.IsFailure)
        {
            return r.Error;
        }
        if (r.Value is not null && parsedMode.Value is not null && !ModeMatches(r.Value.Mode, parsedMode.Value.Value))
        {
            return Error.NotFound(
                "cloudflare.tunnel_mode_mismatch",
                $"El túnel '{r.Value.TunnelId}' no coincide con mode={mode}.");
        }

        return r;
    }

    private async Task<Result<TunnelDnsTarget>> ResolveTunnelDnsTargetAsync(string? tunnelId, string? mode, CancellationToken ct)
    {
        var parsedMode = ParseTunnelMode(mode);
        if (parsedMode.IsFailure)
        {
            return parsedMode.Error;
        }

        if (parsedMode.Value == TunnelMode.Direct || !string.IsNullOrWhiteSpace(tunnelId))
        {
            if (string.IsNullOrWhiteSpace(tunnelId))
            {
                return Error.Validation(
                    "cloudflare.direct_tunnel_id_required",
                    "tunnelId es obligatorio para asegurar DNS de un túnel Direct.");
            }

            var tunnel = await mediator.Send(new GetTunnelQuery(tunnelId), ct).ConfigureAwait(false);
            if (tunnel.IsFailure)
            {
                return tunnel.Error;
            }
            if (tunnel.Value is null)
            {
                return Error.NotFound("tunnel.none", "No hay túnel gestionado registrado.");
            }
            if (parsedMode.Value is not null && !ModeMatches(tunnel.Value.Mode, parsedMode.Value.Value))
            {
                return Error.NotFound(
                    "cloudflare.tunnel_mode_mismatch",
                    $"El túnel '{tunnel.Value.TunnelId}' no coincide con mode={mode}.");
            }

            if (string.Equals(tunnel.Value.Mode, "direct", StringComparison.OrdinalIgnoreCase))
            {
                return new TunnelDnsTarget($"{tunnel.Value.TunnelId}.cfargotunnel.com", Strict: true);
            }
        }

        var tunnelCname = configuration["NativeDeploy:TunnelCname"];
        if (string.IsNullOrWhiteSpace(tunnelCname))
        {
            return Error.Conflict(
                "cloudflare.tunnel_cname_missing",
                "NativeDeploy:TunnelCname no esta configurado; no se puede crear el CNAME.");
        }

        return new TunnelDnsTarget(tunnelCname, Strict: false);
    }

    private static Result<TunnelMode?> ParseTunnelMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return Result<TunnelMode?>.Success(null);
        }

        return mode.Trim().ToLowerInvariant().Replace("-", "_") switch
        {
            "centralproxy" or "central_proxy" => Result<TunnelMode?>.Success(TunnelMode.CentralProxy),
            "direct" => Result<TunnelMode?>.Success(TunnelMode.Direct),
            _ => Error.Validation("cloudflare.tunnel_mode_invalid", "Modo de túnel no soportado."),
        };
    }

    private static bool IsDirectMode(string? mode)
        => string.Equals(mode?.Trim().Replace("-", "_"), "direct", StringComparison.OrdinalIgnoreCase);

    private static bool ModeMatches(string value, TunnelMode mode)
        => mode switch
        {
            TunnelMode.CentralProxy => string.Equals(value, "central_proxy", StringComparison.OrdinalIgnoreCase),
            TunnelMode.Direct => string.Equals(value, "direct", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    private static bool ExistingCnameMatches(string type, string content, string desiredTarget)
        => string.Equals(type, "CNAME", StringComparison.OrdinalIgnoreCase)
            && string.Equals(NormalizeDnsTarget(content), NormalizeDnsTarget(desiredTarget), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDnsTarget(string target)
        => target.Trim().TrimEnd('.');

    private sealed record TunnelDnsTarget(string Target, bool Strict);
}
