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
        if (Context.Request.Headers.TryGetValue(SatelliteAuthSchemes.TokenHeader, out var header))
        {
            var value = header.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        // SignalR WebSocket: el cliente JS y .NET ponen el token en query string `access_token`.
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
