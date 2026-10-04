using Aethra.Modules.Cloudflare.Application.Dtos;
using Aethra.Modules.Cloudflare.Domain;
using Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;
using Aethra.Modules.Cloudflare.UseCases.DnsRecords.Commands;
using Aethra.Modules.Cloudflare.UseCases.DnsRecords.Queries;
using Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands;
using Aethra.Modules.Cloudflare.UseCases.Tunnels.Queries;
using Aethra.Modules.Cloudflare.UseCases.Zones.Commands;
using Aethra.Modules.Cloudflare.UseCases.Zones.Queries;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace Aethra.Modules.Cloudflare.Presentation;

public static class CloudflareEndpoints
{
    private const string ScopeRead = "scope:cloudflare:read";
    private const string ScopeWrite = "scope:cloudflare:write";

    public static IEndpointRouteBuilder MapCloudflareEndpoints(this IEndpointRouteBuilder app)
    {
        var zones = app.MapGroup("/api/cloudflare/zones").WithTags("Cloudflare");

        zones.MapGet("/", async (IMediator m, CancellationToken ct) =>
            ToResult(await m.Send(new ListZonesQuery(), ct)))
            .RequireAuthorization(ScopeRead)
            .WithName("ListCloudflareZones");

        zones.MapGet("/{zoneId}", async (string zoneId, IMediator m, CancellationToken ct) =>
            ToResult(await m.Send(new GetZoneByIdQuery(zoneId), ct)))
            .RequireAuthorization(ScopeRead)
            .WithName("GetCloudflareZone");

        zones.MapPost("/", async ([FromBody] RegisterZoneRequest body, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new RegisterZoneCommand(body.ZoneId, body.ApiToken), ct);
            return r.IsSuccess
                ? Results.Created($"/api/cloudflare/zones/{r.Value.Id}", r.Value)
                : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("RegisterCloudflareZone");

        zones.MapDelete("/{zoneId}", async (string zoneId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new DeleteZoneCommand(zoneId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("DeleteCloudflareZone");

        zones.MapPost("/{zoneId}/sync", async (string zoneId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new SyncZoneCommand(zoneId), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("SyncCloudflareZone");

        zones.MapPost("/{zoneId}/rotate-token", async (
            string zoneId,
            [FromBody] RotateTokenRequest body,
            IMediator m,
            CancellationToken ct) =>
        {
            var r = await m.Send(new RotateZoneTokenCommand(zoneId, body.ApiToken), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("RotateCloudflareZoneToken");

        zones.MapGet("/{zoneId}/records", async (string zoneId, IMediator m, CancellationToken ct) =>
            ToResult(await m.Send(new ListDnsRecordsQuery(zoneId), ct)))
            .RequireAuthorization(ScopeRead)
            .WithName("ListCloudflareDnsRecords");

        zones.MapPost("/{zoneId}/records", async (
            string zoneId,
            [FromBody] CreateDnsRecordRequestBody body,
            IMediator m,
            CancellationToken ct) =>
        {
            var cmd = new CreateDnsRecordCommand(
                zoneId,
                body.Type,
                body.Name,
                body.Content,
                body.Ttl ?? 300,
                body.Proxied ?? false,
                body.Comment);
            var r = await m.Send(cmd, ct);
            return r.IsSuccess
                ? Results.Created($"/api/cloudflare/records/{r.Value.Id}", r.Value)
                : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("CreateCloudflareDnsRecord");

        var records = app.MapGroup("/api/cloudflare/records").WithTags("Cloudflare");

        records.MapPatch("/{recordId}", async (
            string recordId,
            [FromBody] UpdateDnsRecordRequestBody body,
            IMediator m,
            CancellationToken ct) =>
        {
            var r = await m.Send(
                new UpdateDnsRecordCommand(recordId, body.Content, body.Ttl, body.Proxied, body.Comment), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("UpdateCloudflareDnsRecord");

        records.MapDelete("/{recordId}", async (string recordId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new DeleteDnsRecordCommand(recordId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("DeleteCloudflareDnsRecord");

        // -----------------------------------------------------------------------------
        // F13.9 — Tunnels gestionados remotamente (ingress vía API, cero blip).
        // -----------------------------------------------------------------------------
        var tunnels = app.MapGroup("/api/cloudflare/tunnel").WithTags("Cloudflare");

        tunnels.MapGet("/", async (
            [FromQuery] string? tunnelId,
            [FromQuery] string? mode,
            [FromQuery] string? targetVmId,
            IMediator m,
            CancellationToken ct) =>
        {
            var r = await ResolveTunnelAsync(m, tunnelId, mode, targetVmId, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : MapError(r.Error);
        })
            .RequireAuthorization(ScopeRead)
            .WithName("GetCloudflareTunnel");

        tunnels.MapDelete("/", async ([FromQuery] string? tunnelId, [FromQuery] string? mode, IMediator m, CancellationToken ct) =>
        {
            var parsedMode = ParseTunnelMode(mode);
            if (parsedMode.IsFailure)
            {
                return MapError(parsedMode.Error);
            }
            if (parsedMode.Value == TunnelMode.Direct && string.IsNullOrWhiteSpace(tunnelId))
            {
                return MapError(Error.Validation(
                    "cloudflare.direct_tunnel_id_required",
                    "tunnelId es obligatorio para borrar un túnel Direct desde este endpoint."));
            }

            var r = await m.Send(new DeleteTunnelCommand(tunnelId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("DeleteCloudflareTunnel");

        tunnels.MapPost("/", async ([FromBody] RegisterTunnelRequest body, IMediator m, CancellationToken ct) =>
        {
            var mode = ParseTunnelMode(body.Mode);
            if (mode.IsFailure)
            {
                return MapError(mode.Error);
            }

            var r = await m.Send(new RegisterTunnelCommand(
                body.AccountId, body.TunnelId, body.Name, body.ApiToken,
                body.AethraService, body.FallbackService, body.FallbackNoTlsVerify ?? true,
                body.TargetVmId, mode.Value), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("RegisterCloudflareTunnel");

        tunnels.MapPost("/ingress", async ([FromBody] SetTunnelIngressRequest body, IMediator m, CancellationToken ct) =>
        {
            var parsedMode = ParseTunnelMode(body.Mode);
            if (parsedMode.IsFailure)
            {
                return MapError(parsedMode.Error);
            }
            if (parsedMode.Value == TunnelMode.Direct && string.IsNullOrWhiteSpace(body.TunnelId))
            {
                return MapError(Error.Validation(
                    "cloudflare.direct_tunnel_id_required",
                    "tunnelId es obligatorio para reemplazar ingress de un túnel Direct."));
            }

            var rules = (body.Ingress ?? [])
                .Select(r => new TunnelIngressRule(string.IsNullOrWhiteSpace(r.Hostname) ? null : r.Hostname, r.Service, r.NoTlsVerify ?? false))
                .ToList();
            var r = await m.Send(new SetTunnelIngressCommand(rules, body.TunnelId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("SetCloudflareTunnelIngress");

        tunnels.MapPost("/promote-remote", async (IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new PromoteTunnelRemoteCommand(), ct);
            return r.IsSuccess ? Results.Ok(new { rules = r.Value, source = "cloudflare" }) : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("PromoteCloudflareTunnelRemote");

        tunnels.MapPost("/ensure-hostname", async (
            [FromBody] TunnelHostnameRequest body,
            IMediator m,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            var dnsTarget = await ResolveTunnelDnsTargetAsync(m, configuration, body.TunnelId, body.Mode, ct);
            if (dnsTarget.IsFailure)
            {
                return MapError(dnsTarget.Error);
            }

            var r = await m.Send(new EnsureTunnelHostnameCommand(body.Hostname, body.TunnelId, body.Service), ct);
            if (r.IsFailure)
            {
                return MapError(r.Error);
            }

            var hostname = body.Hostname.Trim();
            var zones = await m.Send(new ListZonesQuery(), ct);
            if (zones.IsFailure)
            {
                return MapError(zones.Error);
            }
            var zone = zones.Value
                .Where(z => hostname.Equals(z.Name, StringComparison.OrdinalIgnoreCase)
                    || hostname.EndsWith("." + z.Name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(z => z.Name.Length)
                .FirstOrDefault();
            if (zone is null)
            {
                return MapError(Error.NotFound(
                    "cloudflare.zone_not_found_for_hostname",
                    $"No hay una zona Cloudflare registrada que cubra '{hostname}'."));
            }

            var records = await m.Send(new ListDnsRecordsQuery(zone.Id), ct);
            if (records.IsFailure)
            {
                return MapError(records.Error);
            }
            var existing = records.Value
                .FirstOrDefault(x => string.Equals(x.Name, hostname, StringComparison.OrdinalIgnoreCase));
            var tunnelCname = dnsTarget.Value.Target;
            if (dnsTarget.Value.Strict)
            {
                if (existing is not null && !ExistingCnameMatches(existing.Type, existing.Content, tunnelCname))
                {
                    return MapError(Error.Conflict(
                        "cloudflare.hostname_record_conflict",
                        $"El record existente para '{hostname}' no apunta al túnel esperado '{NormalizeDnsTarget(tunnelCname)}'."));
                }
            }
            else if (existing is not null
                && !string.Equals(existing.Type, "CNAME", StringComparison.OrdinalIgnoreCase))
            {
                return MapError(Error.Conflict(
                    "cloudflare.hostname_record_conflict",
                    $"Ya existe un record {existing.Type} para '{hostname}'."));
            }
            if (existing is null)
            {
                var created = await m.Send(new CreateDnsRecordCommand(
                    zone.Id, "CNAME", hostname, tunnelCname, 1, true,
                    "aethra tunnel ensure-hostname"), ct);
                return created.IsSuccess ? Results.NoContent() : MapError(created.Error);
            }

            if (!string.Equals(existing.Content, tunnelCname, StringComparison.OrdinalIgnoreCase)
                || existing.Ttl != 1
                || !existing.Proxied)
            {
                var updated = await m.Send(new UpdateDnsRecordCommand(
                    existing.Id, tunnelCname, 1, true, existing.Comment), ct);
                return updated.IsSuccess ? Results.NoContent() : MapError(updated.Error);
            }

            return Results.NoContent();
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("EnsureCloudflareTunnelHostname");

        tunnels.MapPost("/remove-hostname", async ([FromBody] TunnelHostnameRequest body, IMediator m, CancellationToken ct) =>
        {
            var parsedMode = ParseTunnelMode(body.Mode);
            if (parsedMode.IsFailure)
            {
                return MapError(parsedMode.Error);
            }
            if (parsedMode.Value == TunnelMode.Direct && string.IsNullOrWhiteSpace(body.TunnelId))
            {
                return MapError(Error.Validation(
                    "cloudflare.direct_tunnel_id_required",
                    "tunnelId es obligatorio para quitar hostnames de un túnel Direct."));
            }

            var r = await m.Send(new RemoveTunnelHostnameCommand(body.Hostname, body.TunnelId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("RemoveCloudflareTunnelHostname");

        return app;
    }

    public sealed record RegisterTunnelRequest(
        string AccountId, string TunnelId, string Name, string ApiToken,
        string? AethraService, string? FallbackService, bool? FallbackNoTlsVerify, string? TargetVmId,
        string? Mode = null);
    public sealed record SetTunnelIngressRequest(IReadOnlyList<TunnelIngressItem>? Ingress, string? TunnelId = null, string? Mode = null);
    public sealed record TunnelIngressItem(string? Hostname, string Service, bool? NoTlsVerify);
    public sealed record TunnelHostnameRequest(string Hostname, string? TunnelId = null, string? Mode = null, string? Service = null);

    public sealed record RegisterZoneRequest(string ZoneId, string ApiToken);
    public sealed record RotateTokenRequest(string ApiToken);
    public sealed record CreateDnsRecordRequestBody(
        string Type,
        string Name,
        string Content,
        int? Ttl,
        bool? Proxied,
        string? Comment);
    public sealed record UpdateDnsRecordRequestBody(
        string? Content,
        int? Ttl,
        bool? Proxied,
        string? Comment);

    private static IResult ToResult<T>(Result<T> r)
        => r.IsSuccess ? Results.Ok(r.Value) : MapError(r.Error);

    private static async Task<Result<CloudflareTunnelDto?>> ResolveTunnelAsync(
        IMediator mediator,
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

            return await mediator.Send(new GetTunnelForVmQuery(targetVmId.Trim()), ct);
        }

        var r = await mediator.Send(new GetTunnelQuery(tunnelId), ct);
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

    private static async Task<Result<TunnelDnsTarget>> ResolveTunnelDnsTargetAsync(
        IMediator mediator,
        IConfiguration configuration,
        string? tunnelId,
        string? mode,
        CancellationToken ct)
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

            var tunnel = await mediator.Send(new GetTunnelQuery(tunnelId), ct);
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

    private static IResult MapError(Error e) => e.Type switch
    {
        ErrorType.Validation => Results.UnprocessableEntity(new { e.Code, e.Message }),
        ErrorType.NotFound => Results.NotFound(new { e.Code, e.Message }),
        ErrorType.Conflict => Results.Conflict(new { e.Code, e.Message }),
        _ => Results.Problem(e.Message),
    };
}
