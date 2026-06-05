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

        tunnels.MapGet("/", async (IMediator m, CancellationToken ct) =>
            Results.Ok((await m.Send(new GetTunnelQuery(), ct)).Value))
            .RequireAuthorization(ScopeRead)
            .WithName("GetCloudflareTunnel");

        tunnels.MapDelete("/", async (IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new DeleteTunnelCommand(), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("DeleteCloudflareTunnel");

        tunnels.MapPost("/", async ([FromBody] RegisterTunnelRequest body, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new RegisterTunnelCommand(
                body.AccountId, body.TunnelId, body.Name, body.ApiToken,
                body.AethraService, body.FallbackService, body.FallbackNoTlsVerify ?? true, body.TargetVmId), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("RegisterCloudflareTunnel");

        tunnels.MapPost("/ingress", async ([FromBody] SetTunnelIngressRequest body, IMediator m, CancellationToken ct) =>
        {
            var rules = (body.Ingress ?? [])
                .Select(r => new TunnelIngressRule(string.IsNullOrWhiteSpace(r.Hostname) ? null : r.Hostname, r.Service, r.NoTlsVerify ?? false))
                .ToList();
            var r = await m.Send(new SetTunnelIngressCommand(rules), ct);
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

        tunnels.MapPost("/ensure-hostname", async ([FromBody] TunnelHostnameRequest body, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new EnsureTunnelHostnameCommand(body.Hostname), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("EnsureCloudflareTunnelHostname");

        tunnels.MapPost("/remove-hostname", async ([FromBody] TunnelHostnameRequest body, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new RemoveTunnelHostnameCommand(body.Hostname), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("RemoveCloudflareTunnelHostname");

        return app;
    }

    public sealed record RegisterTunnelRequest(
        string AccountId, string TunnelId, string Name, string ApiToken,
        string? AethraService, string? FallbackService, bool? FallbackNoTlsVerify, string? TargetVmId);
    public sealed record SetTunnelIngressRequest(IReadOnlyList<TunnelIngressItem>? Ingress);
    public sealed record TunnelIngressItem(string? Hostname, string Service, bool? NoTlsVerify);
    public sealed record TunnelHostnameRequest(string Hostname);

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

    private static IResult MapError(Error e) => e.Type switch
    {
        ErrorType.Validation => Results.UnprocessableEntity(new { e.Code, e.Message }),
        ErrorType.NotFound => Results.NotFound(new { e.Code, e.Message }),
        ErrorType.Conflict => Results.Conflict(new { e.Code, e.Message }),
        _ => Results.Problem(e.Message),
    };
}
