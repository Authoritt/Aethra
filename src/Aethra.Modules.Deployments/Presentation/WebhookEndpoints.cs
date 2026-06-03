using System.Text.Json;
using Aethra.Modules.Deployments.Domain.Build;
using Aethra.Modules.Deployments.UseCases.Build.Commands;
using Aethra.Modules.Deployments.Webhooks;
using Aethra.Shared.Contracts.Identity;
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
/// F12.3 agrega soporte para <c>pull_request</c> events:
/// - <c>opened</c>/<c>reopened</c>: crear Instance ephemeral + trigger Build sobre refs/pull/N/head.
/// - <c>synchronize</c>: trigger Build (el fan-out por TrackedRef redeploya la Instance existente).
/// - <c>closed</c>: borrar Instance ephemeral (cascade route/dns/container cleanup).
///
/// Endpoint ANÓNIMO (lo llama GitHub). El secret HMAC hace de autenticación.
/// </summary>
public static class WebhookEndpoints
{
    // Cacheado (CA1869): instancia única reutilizada en cada request.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>F12.3 — Label que fuerza preview deploy aunque el Template tenga AutoPreviewPullRequests=false.</summary>
    private const string PreviewLabel = "aethra-preview";

    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/git", HandleAsync)
            .AllowAnonymous()
            .WithTags("Deployments")
            .WithName("GitWebhook")
            .WithDescription("Fan-out de push/pull_request events hacia Builds y previews.")
            .DisableAntiforgery();
        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        ITemplateLookup lookup,
        IMediator mediator,
        IGitHubUserResolver gitHubUsers,
        IPreviewInstanceCoordinator previewCoordinator,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("GitWebhook");

        // 1. Body raw para HMAC.
        using var ms = new MemoryStream();
        await http.Request.Body.CopyToAsync(ms, ct).ConfigureAwait(false);
        var body = ms.ToArray();

        // Routing por X-GitHub-Event. Si está ausente, asumimos push para compatibilidad.
        var eventName = http.Request.Headers["X-GitHub-Event"].ToString();
        if (string.IsNullOrWhiteSpace(eventName)
            || string.Equals(eventName, "ping", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new { event_name = eventName, handled = false });
        }

        return eventName switch
        {
            "push" => await HandlePushAsync(http, body, lookup, mediator, logger, ct).ConfigureAwait(false),
            "pull_request" => await HandlePullRequestAsync(
                http, body, lookup, mediator, gitHubUsers, previewCoordinator, logger, ct).ConfigureAwait(false),
            _ => Results.Ok(new { event_name = eventName, handled = false }),
        };
    }

    private static async Task<IResult> HandlePushAsync(
        HttpContext http,
        byte[] body,
        ITemplateLookup lookup,
        IMediator mediator,
        ILogger logger,
        CancellationToken ct)
    {
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

        // 2. Lookup de Templates candidatos.
        var matchingTemplates = new List<TemplateForBuildView>();
        foreach (var url in payload.CandidateRepoUrls())
        {
            // F12.3 — el branch reportado por GitHub matchea contra Template.DefaultBranch
            // como filtro grueso; la decisión fina (TrackedRef) la hace BuildCompletedHandler.
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

        // 3. Validar firma con el WebhookSecret del primer Template.
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

    private static async Task<IResult> HandlePullRequestAsync(
        HttpContext http,
        byte[] body,
        ITemplateLookup lookup,
        IMediator mediator,
        IGitHubUserResolver gitHubUsers,
        IPreviewInstanceCoordinator coordinator,
        ILogger logger,
        CancellationToken ct)
    {
        GitHubPullRequestPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<GitHubPullRequestPayload>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Webhook PR payload inválido");
            return Results.BadRequest(new { error = "invalid_json" });
        }
        if (payload is null
            || payload.Action is null
            || payload.PullRequest is null
            || payload.PullRequest.Head is null
            || payload.PullRequest.Number is null
            || payload.Repository is null)
        {
            return Results.BadRequest(new { error = "missing_fields" });
        }

        // 1. Match Templates por repo. NOTA: no filtramos por branch — un PR puede tener
        // base.ref diferente al DefaultBranch. Buscamos cualquier Template configurado al repo.
        var matchingTemplates = new List<TemplateForBuildView>();
        foreach (var url in payload.CandidateRepoUrls())
        {
            var templates = await lookup.FindAllByRepoAsync(url, ct).ConfigureAwait(false);
            if (templates.Count > 0)
            {
                matchingTemplates.AddRange(templates);
                break;
            }
        }
        if (matchingTemplates.Count == 0)
        {
            logger.LogInformation("PR webhook para repo sin Templates: {Repo}", payload.Repository.CloneUrl);
            return Results.Ok(new { matched_templates = 0 });
        }

        // 2. Validar HMAC con el primer Template (convención monorepo, igual que push).
        var signatureHeader = http.Request.Headers["X-Hub-Signature-256"].ToString();
        var sharedSecret = matchingTemplates[0].WebhookSecret;
        if (!GitHubSignatureValidator.Validate(signatureHeader, body, sharedSecret))
        {
            logger.LogWarning("PR webhook firma inválida desde {Ip}", http.Connection.RemoteIpAddress);
            return Results.Unauthorized();
        }

        var prNumber = payload.PullRequest.Number.Value;
        var headSha = payload.PullRequest.Head.Sha;
        var prRef = $"refs/pull/{prNumber}/head";

        // 3. Router por action.
        return payload.Action switch
        {
            "opened" or "reopened" => await OpenOrReopenAsync(
                payload, matchingTemplates, prNumber, headSha, prRef,
                mediator, gitHubUsers, coordinator, logger, ct).ConfigureAwait(false),
            "synchronize" => await SynchronizeAsync(
                matchingTemplates, prNumber, headSha, prRef, mediator, logger, ct).ConfigureAwait(false),
            "closed" => await ClosedAsync(matchingTemplates, prNumber, coordinator, logger, ct).ConfigureAwait(false),
            _ => Results.Ok(new { action = payload.Action, handled = false }),
        };
    }

    private static async Task<IResult> OpenOrReopenAsync(
        GitHubPullRequestPayload payload,
        IReadOnlyList<TemplateForBuildView> matchingTemplates,
        int prNumber,
        string? headSha,
        string prRef,
        IMediator mediator,
        IGitHubUserResolver gitHubUsers,
        IPreviewInstanceCoordinator coordinator,
        ILogger logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(headSha))
        {
            return Results.BadRequest(new { error = "missing_head_sha" });
        }
        var prAuthorLogin = payload.PullRequest?.User?.Login;
        var hasLabel = payload.PullRequest?.Labels.Any(l =>
            string.Equals(l.Name, PreviewLabel, StringComparison.OrdinalIgnoreCase)) ?? false;

        // Mapear PR author → User Aethra (puede ser null si no se configuró handle).
        string? createdByUserId = null;
        if (!string.IsNullOrWhiteSpace(prAuthorLogin))
        {
            createdByUserId = await gitHubUsers.FindByGitHubUsernameAsync(prAuthorLogin, ct).ConfigureAwait(false);
        }

        var processed = new List<object>();
        foreach (var tpl in matchingTemplates)
        {
            // Filtro fino: opt-in del Template O label aethra-preview en el PR.
            var optIn = tpl.AutoPreviewPullRequests || hasLabel;
            if (!optIn)
            {
                processed.Add(new
                {
                    template_id = tpl.TemplateId,
                    action = "skipped",
                    reason = "auto_preview_disabled_and_no_label",
                });
                continue;
            }

            // Si el author no está mapeado, skip con razón clara — la UI puede leer el log.
            if (createdByUserId is null)
            {
                processed.Add(new
                {
                    template_id = tpl.TemplateId,
                    action = "skipped",
                    reason = "github_user_not_mapped",
                    github_login = prAuthorLogin,
                });
                continue;
            }

            // Provisioning de la Instance ephemeral (idempotente).
            var prov = await coordinator.EnsurePreviewAsync(tpl.TemplateId, prNumber, headSha, createdByUserId, ct)
                .ConfigureAwait(false);
            if (prov.Status is PreviewProvisioningStatus.QuotaExceeded)
            {
                processed.Add(new
                {
                    template_id = tpl.TemplateId,
                    action = "skipped",
                    reason = "quota_exceeded",
                    current = prov.QuotaActual,
                    max = prov.QuotaMax,
                });
                continue;
            }
            if (prov.Status is PreviewProvisioningStatus.NoVmAvailable)
            {
                processed.Add(new { template_id = tpl.TemplateId, action = "skipped", reason = "no_preview_vm_available" });
                continue;
            }
            if (prov.Status is PreviewProvisioningStatus.PreviewsDisabled)
            {
                processed.Add(new { template_id = tpl.TemplateId, action = "skipped", reason = "previews_disabled" });
                continue;
            }
            if (prov.Status is PreviewProvisioningStatus.TemplateNotFound)
            {
                processed.Add(new { template_id = tpl.TemplateId, action = "skipped", reason = "template_not_found" });
                continue;
            }

            // Disparar build sobre refs/pull/N/head — el fan-out de BuildCompletedHandler
            // filtra por TrackedRef y solo redeploya la Instance ephemeral creada.
            var cmd = new TriggerBuildCommand(
                TemplateId: tpl.TemplateId,
                GitSha: headSha,
                GitRef: prRef,
                Trigger: BuildTrigger.Webhook,
                TriggeredBy: prAuthorLogin);
            var buildResult = await mediator.Send(cmd, ct).ConfigureAwait(false);
            if (!buildResult.IsSuccess)
            {
                logger.LogWarning("Build PR no se pudo encolar para template {Tpl}: {Code} {Msg}",
                    tpl.TemplateId, buildResult.Error.Code, buildResult.Error.Message);
            }
            processed.Add(new
            {
                template_id = tpl.TemplateId,
                action = prov.Status == PreviewProvisioningStatus.Created ? "created" : "reused",
                instance_id = prov.InstanceId,
                hostname = prov.Hostname,
                build_queued = buildResult.IsSuccess,
            });
        }

        return Results.Ok(new
        {
            action = payload.Action,
            pr_number = prNumber,
            results = processed,
        });
    }

    private static async Task<IResult> SynchronizeAsync(
        IReadOnlyList<TemplateForBuildView> matchingTemplates,
        int prNumber,
        string? headSha,
        string prRef,
        IMediator mediator,
        ILogger logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(headSha))
        {
            return Results.BadRequest(new { error = "missing_head_sha" });
        }
        var processed = new List<object>();
        foreach (var tpl in matchingTemplates)
        {
            var cmd = new TriggerBuildCommand(
                TemplateId: tpl.TemplateId,
                GitSha: headSha,
                GitRef: prRef,
                Trigger: BuildTrigger.Webhook,
                TriggeredBy: null);
            var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
            processed.Add(new
            {
                template_id = tpl.TemplateId,
                build_queued = result.IsSuccess,
                error = result.IsSuccess ? null : result.Error.Code,
            });
            if (!result.IsSuccess)
            {
                logger.LogWarning("Build PR sync no encolado {Tpl}: {Code}", tpl.TemplateId, result.Error.Code);
            }
        }
        return Results.Ok(new { action = "synchronize", pr_number = prNumber, results = processed });
    }

    private static async Task<IResult> ClosedAsync(
        IReadOnlyList<TemplateForBuildView> matchingTemplates,
        int prNumber,
        IPreviewInstanceCoordinator coordinator,
        ILogger logger,
        CancellationToken ct)
    {
        var processed = new List<object>();
        foreach (var tpl in matchingTemplates)
        {
            var teardown = await coordinator.TeardownPreviewAsync(tpl.TemplateId, prNumber, ct).ConfigureAwait(false);
            processed.Add(new
            {
                template_id = tpl.TemplateId,
                status = teardown.Status.ToString(),
                instance_id = teardown.InstanceId,
            });
            if (teardown.Status == PreviewTeardownStatus.Removed)
            {
                logger.LogInformation("Preview {Inst} (PR #{N}) eliminada", teardown.InstanceId, prNumber);
            }
        }
        return Results.Ok(new { action = "closed", pr_number = prNumber, results = processed });
    }
}
