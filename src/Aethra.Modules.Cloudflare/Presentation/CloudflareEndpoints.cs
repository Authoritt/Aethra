using Aethra.Modules.Cloudflare.UseCases.DnsRecords.Commands;
using Aethra.Modules.Cloudflare.UseCases.DnsRecords.Queries;
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
    public static IEndpointRouteBuilder MapCloudflareEndpoints(this IEndpointRouteBuilder app)
    {
        var zones = app.MapGroup("/api/cloudflare/zones").WithTags("Cloudflare").RequireAuthorization();

        zones.MapGet("/", async (IMediator m, CancellationToken ct) =>
            ToResult(await m.Send(new ListZonesQuery(), ct)))
            .WithName("ListCloudflareZones");

        zones.MapGet("/{zoneId}", async (string zoneId, IMediator m, CancellationToken ct) =>
            ToResult(await m.Send(new GetZoneByIdQuery(zoneId), ct)))
            .WithName("GetCloudflareZone");

        zones.MapPost("/", async ([FromBody] RegisterZoneRequest body, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new RegisterZoneCommand(body.ZoneId, body.ApiToken), ct);
            return r.IsSuccess
                ? Results.Created($"/api/cloudflare/zones/{r.Value.Id}", r.Value)
                : MapError(r.Error);
        }).WithName("RegisterCloudflareZone");

        zones.MapDelete("/{zoneId}", async (string zoneId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new DeleteZoneCommand(zoneId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithName("DeleteCloudflareZone");

        zones.MapPost("/{zoneId}/sync", async (string zoneId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new SyncZoneCommand(zoneId), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : MapError(r.Error);
        }).WithName("SyncCloudflareZone");

        zones.MapPost("/{zoneId}/rotate-token", async (
            string zoneId,
            [FromBody] RotateTokenRequest body,
            IMediator m,
            CancellationToken ct) =>
        {
            var r = await m.Send(new RotateZoneTokenCommand(zoneId, body.ApiToken), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithName("RotateCloudflareZoneToken");

        zones.MapGet("/{zoneId}/records", async (string zoneId, IMediator m, CancellationToken ct) =>
            ToResult(await m.Send(new ListDnsRecordsQuery(zoneId), ct)))
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
        }).WithName("CreateCloudflareDnsRecord");

        var records = app.MapGroup("/api/cloudflare/records").WithTags("Cloudflare").RequireAuthorization();

        records.MapPatch("/{recordId}", async (
            string recordId,
            [FromBody] UpdateDnsRecordRequestBody body,
            IMediator m,
            CancellationToken ct) =>
        {
            var r = await m.Send(
                new UpdateDnsRecordCommand(recordId, body.Content, body.Ttl, body.Proxied, body.Comment), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : MapError(r.Error);
        }).WithName("UpdateCloudflareDnsRecord");

        records.MapDelete("/{recordId}", async (string recordId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new DeleteDnsRecordCommand(recordId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithName("DeleteCloudflareDnsRecord");

        return app;
    }

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
