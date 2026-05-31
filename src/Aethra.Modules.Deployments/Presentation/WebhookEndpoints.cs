using System.Text.Json;
using Aethra.Modules.Deployments.Domain;
using Aethra.Modules.Deployments.UseCases.Commands;
using Aethra.Modules.Deployments.Webhooks;
using Aethra.Shared.Contracts.Projects;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Deployments.Presentation;

/// <summary>
/// Endpoint webhook de Git providers (por ahora GitHub).
///
/// Flujo:
/// 1. Recibe payload raw (sin model-binding para preservar bytes exactos del HMAC).
/// 2. Parsea para obtener repoUrl + branch.
/// 3. Hace lookup de Applications que apuntan a ese (repo, branch) via <see cref="IApplicationLookup"/>.
/// 4. Toma el primer WebhookSecret no vacío (convención: todas las apps de un mismo repo comparten secret).
/// 5. Valida HMAC SHA-256 del body contra ese secret.
/// 6. Para cada app: matchea paths afectados contra WatchPaths. Si matchea, encola DeployJob.
///
/// Endpoint ANÓNIMO (lo llama GitHub). El secret hace de auth.
/// </summary>
public static class WebhookEndpoints
{
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
            .DisableAntiforgery();
        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        IApplicationLookup lookup,
        IMediator mediator,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("GitWebhook");

        // 1. Leer body raw — necesario para HMAC.
        using var ms = new MemoryStream();
        await http.Request.Body.CopyToAsync(ms, ct);
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

        // 2. Lookup Applications candidatas. Probamos varias URLs porque GitHub puede dar
        //    clone_url (https), ssh_url (git@) o html_url y nuestras Apps pueden usar cualquiera.
        var matchingApps = new List<ApplicationForDeployView>();
        foreach (var url in payload.CandidateRepoUrls())
        {
            var apps = await lookup.FindByRepoAsync(url, payload.Branch, ct);
            if (apps.Count > 0)
            {
                matchingApps.AddRange(apps);
                break;
            }
        }
        if (matchingApps.Count == 0)
        {
            logger.LogInformation("Webhook push para repo sin Apps registradas: {Repo} @ {Branch}",
                payload.Repository.CloneUrl, payload.Branch);
            return Results.Ok(new { matched_apps = 0, message = "No hay Apps configuradas para este repo+branch." });
        }

        // 3. Validar firma con el WebhookSecret de las apps (todas las del repo comparten secret).
        var signatureHeader = http.Request.Headers["X-Hub-Signature-256"].ToString();
        var sharedSecret = matchingApps.First().WebhookSecret;
        if (!GitHubSignatureValidator.Validate(signatureHeader, body, sharedSecret))
        {
            logger.LogWarning("Webhook firma inválida desde {Ip}", http.Connection.RemoteIpAddress);
            return Results.Unauthorized();
        }

        // 4. Fan-out: para cada app, matchear WatchPaths y encolar DeployJob.
        var affectedPaths = payload.AllAffectedPaths();
        var commitSha = payload.After ?? payload.HeadCommit?.Id ?? "head";
        var pusher = payload.Pusher?.Name ?? payload.Pusher?.Email;

        var triggered = new List<string>();
        var skipped = new List<string>();

        foreach (var app in matchingApps)
        {
            if (!WatchPathMatcher.AnyMatches(affectedPaths, app.WatchPaths))
            {
                skipped.Add(app.Slug);
                continue;
            }

            var cmd = new TriggerDeployCommand(
                ApplicationId: app.ApplicationId,
                GitSha: commitSha,
                Branch: payload.Branch,
                Trigger: DeployTrigger.Webhook,
                TriggeredBy: pusher);

            var result = await mediator.Send(cmd, ct);
            if (result.IsSuccess)
            {
                triggered.Add(app.Slug);
            }
            else
            {
                logger.LogWarning("No se pudo encolar deploy para {Slug}: {Code} {Msg}",
                    app.Slug, result.Error.Code, result.Error.Message);
            }
        }

        return Results.Ok(new
        {
            matched_apps = matchingApps.Count,
            triggered_apps = triggered,
            skipped_apps = skipped,
            commit_sha = commitSha,
        });
    }
}
