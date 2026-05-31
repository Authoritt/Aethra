using Aethra.Modules.Proxy.Infrastructure.Tls;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Aethra.Modules.Proxy.Presentation;

/// <summary>
/// Endpoint anónimo que sirve los desafíos HTTP-01 de Let's Encrypt. La CA hace un GET
/// a <c>http://{hostname}/.well-known/acme-challenge/{token}</c> y espera el
/// <c>keyAuthorization</c> en texto plano. Sin esto el cert nunca se emite.
/// </summary>
public static class AcmeChallengeEndpoint
{
    public static IEndpointRouteBuilder MapAcmeChallengeEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/acme-challenge/{token}", (string token, IAcmeChallengeStore store) =>
        {
            var keyAuth = store.Get(token);
            return keyAuth is null
                ? Results.NotFound()
                : Results.Text(keyAuth, contentType: "text/plain");
        })
        .AllowAnonymous()
        .WithName("AcmeHttpChallenge")
        .WithTags("Tls")
        .WithDescription("HTTP-01 challenge para Let's Encrypt. No requiere autenticación.");

        return app;
    }
}
