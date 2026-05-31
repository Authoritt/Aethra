using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Aethra.Modules.Deployments.Presentation;

/// <summary>
/// Endpoint webhook de Git providers (GitHub). En el modelo F9 el fan-out apunta a
/// <c>Templates</c> en lugar de <c>Applications</c>, y cada Template tiene su propio
/// WebhookSecret.
///
/// TODO F9.3: refactor para nuevo modelo. Implementación actual stubeada: devuelve 503
/// mientras <see cref="Aethra.Shared.Contracts.Projects.ITemplateLookup"/> sea el NoOp y
/// las migraciones del módulo no estén regeneradas.
///
/// Los helpers (<c>GitHubPushPayload</c>, <c>GitHubSignatureValidator</c>,
/// <c>WatchPathMatcher</c>) se preservan en <c>Webhooks/</c> para reutilizarlos en F9.3.
/// </summary>
public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/git", static () => Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
            .AllowAnonymous()
            .WithTags("Deployments")
            .WithName("GitWebhook")
            .WithDescription("F9.3 reintroducirá el fan-out hacia Templates.")
            .DisableAntiforgery();
        return app;
    }
}
