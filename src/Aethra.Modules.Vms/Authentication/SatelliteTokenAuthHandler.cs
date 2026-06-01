using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aethra.Modules.Vms.Authentication;

public sealed class SatelliteTokenAuthOptions : AuthenticationSchemeOptions { }

/// <summary>
/// Handler que valida el header <c>X-Satellite-Token</c> (o query string <c>access_token</c>
/// para WebSocket donde no se pueden setear headers) y emite un ClaimsPrincipal con el VmId.
/// </summary>
public sealed class SatelliteTokenAuthHandler(
    IOptionsMonitor<SatelliteTokenAuthOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ISatelliteAuthenticator authenticator)
    : AuthenticationHandler<SatelliteTokenAuthOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ResolveToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            return AuthenticateResult.NoResult();
        }

        var vmId = await authenticator.AuthenticateAsync(token, Context.RequestAborted).ConfigureAwait(false);
        if (vmId is null)
        {
            Logger.LogWarning("Satellite token presentado inválido desde {Ip}", Context.Connection.RemoteIpAddress);
            return AuthenticateResult.Fail("Invalid satellite token");
        }

        var identity = new ClaimsIdentity(
        [
            new Claim(SatelliteAuthSchemes.VmIdClaim, vmId.Value.ToString()!),
            new Claim(ClaimTypes.NameIdentifier, vmId.Value.ToString()!),
        ], SatelliteAuthSchemes.TokenHeader);

        return AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(identity), SatelliteAuthSchemes.TokenHeader));
    }

    private string? ResolveToken()
    {
        // 1) Header explícito X-Satellite-Token (curl, tests, scripts).
        if (Context.Request.Headers.TryGetValue(SatelliteAuthSchemes.TokenHeader, out var header))
        {
            var value = header.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        // 2) SignalR HTTP negotiate: el cliente .NET pone el token en Authorization: Bearer
        //    (vía HubConnectionBuilder().WithUrl(..., http => http.AccessTokenProvider = ...)).
        if (Context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var value = authHeader.ToString();
            if (value.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                var token = value["Bearer ".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return token;
                }
            }
        }
        // 3) SignalR WebSocket: el upgrade no permite headers custom, así que el cliente lo pasa
        //    como query string `access_token`.
        if (Context.Request.Query.TryGetValue(SatelliteAuthSchemes.QueryParam, out var q))
        {
            var value = q.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        return null;
    }
}
