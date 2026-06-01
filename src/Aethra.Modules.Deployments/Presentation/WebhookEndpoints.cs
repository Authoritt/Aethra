using System.Text.Json;
using Aethra.Modules.Deployments.Domain.Build;
using Aethra.Modules.Deployments.UseCases.Build.Commands;
using Aethra.Modules.Deployments.Webhooks;
using Aethra.Shared.Contracts.Projects;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Deployments.Presentation;

/// <summary>
/// Endpoint webhook de Git providers (GitHub). En el modelo F9 el fan-out apunta a
/// <c>Templates</c> en lugar de <c>Applications</c>, y cada Template tiene su propio
/// <c>WebhookSecret</c>. Un push a un monorepo puede generar N builds (uno por Template
/// que apunte a ese repo+branch y matchee WatchPaths).
///
/// Flujo:
/// 1. Body raw (sin model-binding) para preservar bytes exactos del HMAC.
/// 2. Parsea el payload.
/// 3. Lookup de Templates por cada URL candidata (clone_url/ssh/html).
/// 4. Si no hay matches → 200 con <c>matched_templates=0</c> (no es error, GitHub puede
///    estar configurado de más).
/// 5. Toma el <c>WebhookSecret</c> del primer Template (convención monorepo: todos los
///    Templates del mismo repo comparten el secret).
/// 6. Valida HMAC SHA-256 del body. Si falla → 401.
/// 7. Por cada Template matching, filtra WatchPaths. Los que matchean se encolan via
///    <c>TriggerBuildCommand</c>.
///
/// Endpoint ANÓNIMO (lo llama GitHub). El secret hace de autenticación.
/// </summary>
public static class WebhookEndpoints
{
    // Cacheado (CA1869): instancia única reutilizada en cada request.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/git", HandleAsync)
            .AllowAnonymous()
            .WithTags("Deployments")
            .WithName("GitWebhook")
            .WithDescription("Fan-out de push event hacia Builds por Template.")
            .DisableAntiforgery();
        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        ITemplateLookup lookup,
        IMediator mediator,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("GitWebhook");

        // 1. Body raw para HMAC.
        using var ms = new MemoryStream();
        await http.Request.Body.CopyToAsync(ms, ct).ConfigureAwait(false);
        var body = ms.ToArray();

        GitHubPushPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<GitHubPushPayload>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Webhook payload no es JSON válido");
            return Results.BadRequest(new { error = "invalid_json" });
        }

        if (payload is null || payload.Branch is null || payload.Repository is null)
        {
            return Results.BadRequest(new { error = "missing_fields", detail = "ref/repository requeridos" });
        }

        var headSha = payload.HeadSha;
        if (string.IsNullOrWhiteSpace(headSha))
        {
            return Results.BadRequest(new { error = "missing_head_sha", detail = "after o head_commit.id requeridos" });
        }

        // 2. Lookup de Templates candidatos. Probamos clone_url / ssh_url / html_url porque
        // los operadores configuran Templates con cualquiera de los tres formatos.
        var matchingTemplates = new List<TemplateForBuildView>();
        foreach (var url in payload.CandidateRepoUrls())
        {
            var templates = await lookup.FindByRepoAsync(url, payload.Branch, ct).ConfigureAwait(false);
            if (templates.Count > 0)
            {
                matchingTemplates.AddRange(templates);
                break;
            }
        }

        if (matchingTemplates.Count == 0)
        {
            logger.LogInformation("Webhook push para repo sin Templates registrados: {Repo} @ {Branch}",
                payload.Repository.CloneUrl, payload.Branch);
            return Results.Ok(new { matched_templates = 0 });
        }

        // 3. Validar firma con el WebhookSecret del primer Template (convención monorepo).
        var signatureHeader = http.Request.Headers["X-Hub-Signature-256"].ToString();
        var sharedSecret = matchingTemplates[0].WebhookSecret;
        if (!GitHubSignatureValidator.Validate(signatureHeader, body, sharedSecret))
        {
            logger.LogWarning("Webhook firma inválida desde {Ip}", http.Connection.RemoteIpAddress);
            return Results.Unauthorized();
        }

        // 4. Fan-out: por cada Template, evaluar WatchPaths y encolar Build.
        var affectedPaths = payload.AllAffectedPaths();
        var pusher = payload.Pusher?.Name ?? payload.Pusher?.Email;
        var triggered = new List<string>();
        var skipped = new List<string>();

        foreach (var tpl in matchingTemplates)
        {
            if (!WatchPathMatcher.AnyMatches(affectedPaths, tpl.WatchPaths))
            {
                skipped.Add(tpl.TemplateId);
                continue;
            }

            var cmd = new TriggerBuildCommand(
                TemplateId: tpl.TemplateId,
                GitSha: headSha,
                GitRef: payload.Ref!,
                Trigger: BuildTrigger.Webhook,
                TriggeredBy: pusher);

            var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                triggered.Add(tpl.TemplateId);
            }
            else
            {
                logger.LogWarning("No se pudo encolar build para template {Tpl}: {Code} {Msg}",
                    tpl.TemplateId, result.Error.Code, result.Error.Message);
            }
        }

        return Results.Ok(new
        {
            matched_templates = matchingTemplates.Count,
            triggered_template_ids = triggered,
            skipped_template_ids = skipped,
            head_sha = headSha,
        });
    }
}
