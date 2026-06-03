using System.Security.Claims;
using Aethra.Modules.Identity.UseCases.Totp;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Aethra.Modules.Identity.Presentation;

/// <summary>
/// F12.1B — endpoints para que un usuario humano (cookie auth) gestione su propio 2FA TOTP.
/// Todos requieren auth por cookie ("CookieOnly" policy) — NO se exponen a API keys/MCP por
/// sensibilidad (gestionar 2FA debe pasar por la UI del usuario, no por un agente IA).
/// </summary>
public static class TotpEndpoints
{
    public static IEndpointRouteBuilder MapTotpEndpoints(this IEndpointRouteBuilder app)
    {
        var totp = app.MapGroup("/api/identity/me/totp")
            .WithTags("Identity")
            .RequireAuthorization("CookieOnly");

        // POST /enroll — genera secret y devuelve QR/secret. NO activa 2FA todavia.
        totp.MapPost("/enroll", async (HttpContext http, IMediator m, CancellationToken ct) =>
        {
            var uid = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid))
            {
                return Results.Json(new { error = "not_authenticated" }, statusCode: 401);
            }
            var r = await m.Send(new EnrollTotpCommand(uid), ct);
            return r.IsSuccess
                ? Results.Ok(new
                {
                    otpauth_uri = r.Value.OtpAuthUri,
                    secret_b32 = r.Value.SecretBase32,
                    issuer = r.Value.Issuer,
                    account = r.Value.Account,
                })
                : MapError(r.Error);
        })
        .WithName("EnrollTotp");

        // POST /verify — verifica el codigo y activa 2FA, devuelve recovery codes (UNA sola vez).
        totp.MapPost("/verify", async (HttpContext http, [FromBody] VerifyTotpRequest body,
            IMediator m, CancellationToken ct) =>
        {
            var uid = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid))
            {
                return Results.Json(new { error = "not_authenticated" }, statusCode: 401);
            }
            var r = await m.Send(new VerifyTotpEnrollmentCommand(uid, body.Code), ct);
            return r.IsSuccess
                ? Results.Ok(new { enabled = r.Value.Enabled, recovery_codes = r.Value.RecoveryCodes })
                : MapError(r.Error);
        })
        .WithName("VerifyTotpEnrollment");

        // POST /disable — desactiva 2FA. Requiere code valido.
        totp.MapPost("/disable", async (HttpContext http, [FromBody] DisableTotpRequest body,
            IMediator m, CancellationToken ct) =>
        {
            var uid = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid))
            {
                return Results.Json(new { error = "not_authenticated" }, statusCode: 401);
            }
            var r = await m.Send(new DisableTotpCommand(uid, body.Code), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .WithName("DisableTotp");

        // POST /regenerate-recovery-codes — genera nuevos 10 recovery codes.
        totp.MapPost("/regenerate-recovery-codes", async (HttpContext http,
            [FromBody] RegenerateRecoveryRequest body, IMediator m, CancellationToken ct) =>
        {
            var uid = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid))
            {
                return Results.Json(new { error = "not_authenticated" }, statusCode: 401);
            }
            var r = await m.Send(new RegenerateRecoveryCodesCommand(uid, body.Code), ct);
            return r.IsSuccess
                ? Results.Ok(new { recovery_codes = r.Value.RecoveryCodes })
                : MapError(r.Error);
        })
        .WithName("RegenerateRecoveryCodes");

        return app;
    }

    public sealed record VerifyTotpRequest(string Code);
    public sealed record DisableTotpRequest(string Code);
    public sealed record RegenerateRecoveryRequest(string Code);

    private static IResult MapError(Error e) => e.Type switch
    {
        ErrorType.Validation => Results.UnprocessableEntity(new { e.Code, e.Message }),
        ErrorType.NotFound => Results.NotFound(new { e.Code, e.Message }),
        ErrorType.Conflict => Results.Conflict(new { e.Code, e.Message }),
        ErrorType.Forbidden => Results.Json(new { e.Code, e.Message }, statusCode: StatusCodes.Status403Forbidden),
        _ => Results.Problem(e.Message),
    };
}
