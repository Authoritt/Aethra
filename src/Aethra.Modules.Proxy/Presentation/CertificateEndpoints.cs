using Aethra.Modules.Proxy.Domain;
using Aethra.Modules.Proxy.Infrastructure;
using Aethra.Modules.Proxy.Infrastructure.Tls;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Primitives;
using Aethra.Shared.Kernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Proxy.Presentation;

public static class CertificateEndpoints
{
    private const string ScopeRead = "scope:proxy:read";
    private const string ScopeWrite = "scope:proxy:write";

    public static IEndpointRouteBuilder MapCertificateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/proxy/certificates").WithTags("Proxy");

        group.MapGet("/", async (ProxyDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Certificates.AsNoTracking()
                .OrderBy(c => c.Hostname)
                .Select(c => new CertificateDto(
                    c.Id.ToString(),
                    c.Hostname.Value,
                    c.Status.ToString().ToLowerInvariant(),
                    c.IssuedAt,
                    c.NotBefore,
                    c.NotAfter,
                    c.RenewAfter,
                    c.LastError))
                .ToListAsync(ct)
                .ConfigureAwait(false);
            return Results.Ok(rows);
        })
        .RequireAuthorization(ScopeRead)
        .WithName("ListCertificates");

        group.MapPost("/request", async (
            [FromBody] RequestCertificateRequest body,
            ICertManager manager,
            CancellationToken ct) =>
        {
            var hostnameResult = Hostname.Create(body.Hostname);
            if (!hostnameResult.IsSuccess)
            {
                return MapError(hostnameResult.Error);
            }

            var result = await manager.RequestAsync(hostnameResult.Value, ct).ConfigureAwait(false);
            return ToResult(result);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("RequestCertificate");

        group.MapPost("/{certificateId}/renew", async (
            string certificateId,
            ICertManager manager,
            CancellationToken ct) =>
        {
            if (!TryCertificateId(certificateId, out var typed))
            {
                return Results.UnprocessableEntity(new
                {
                    Code = "certificate.invalid_id",
                    Message = "CertificateId invalido."
                });
            }

            var result = await manager.RenewAsync(typed, ct).ConfigureAwait(false);
            return ToResult(result);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("RenewCertificate");

        return app;
    }

    private static IResult ToResult(Result<Certificate> result)
        => result.IsSuccess
            ? Results.Ok(ToDto(result.Value))
            : MapError(result.Error);

    private static CertificateDto ToDto(Certificate c)
        => new(
            c.Id.ToString(),
            c.Hostname.Value,
            c.Status.ToString().ToLowerInvariant(),
            c.IssuedAt,
            c.NotBefore,
            c.NotAfter,
            c.RenewAfter,
            c.LastError);

    private static bool TryCertificateId(string raw, out CertificateId id)
    {
        id = default;
        if (!AethraId.TryParse(raw, out var parsed) || parsed.Value.Prefix != "cert")
        {
            return false;
        }

        id = new CertificateId(parsed.Value);
        return true;
    }

    private static IResult MapError(Error e) => e.Type switch
    {
        ErrorType.Validation => Results.UnprocessableEntity(new { e.Code, e.Message }),
        ErrorType.NotFound => Results.NotFound(new { e.Code, e.Message }),
        ErrorType.Conflict => Results.Conflict(new { e.Code, e.Message }),
        _ => Results.Problem(e.Message),
    };

    public sealed record RequestCertificateRequest(string Hostname);

    public sealed record CertificateDto(
        string Id,
        string Hostname,
        string Status,
        DateTimeOffset? IssuedAt,
        DateTimeOffset? NotBefore,
        DateTimeOffset? NotAfter,
        DateTimeOffset? RenewAfter,
        string? LastError);
}
