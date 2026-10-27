using System.Security.Claims;
using Aethra.Modules.Settings.Domain;
using Aethra.Modules.Settings.UseCases.BaseDomains.Commands;
using Aethra.Modules.Settings.UseCases.BaseDomains.Queries;
using Aethra.Modules.Settings.UseCases.Environments.Commands;
using Aethra.Modules.Settings.UseCases.Environments.Queries;
using Aethra.Modules.Settings.UseCases.IntegrationCredentials.Commands;
using Aethra.Modules.Settings.UseCases.IntegrationCredentials.Queries;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Settings.Presentation;

public static class SettingsEndpoints
{
    /// <summary>
    /// Nombre de la policy que el host registra en <c>apps/api/Program.cs</c> para
    /// credenciales de integración: SOLO cookie de un administrador (no API keys).
    /// Duplicado intencionalmente para no acoplar este módulo a internals del host.
    /// </summary>
    private const string IntegrationCredentialAdminPolicy = "IntegrationCredentialAdmin";

    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        MapIntegrationsEndpoints(app);
        MapDomainsEndpoints(app);
        MapEnvironmentsEndpoints(app);
        return app;
    }

    // ----------------------------------------------------------------------------
    // Integraciones (credenciales externas). SOLO administradores por cookie — una API key
    // ni un usuario ordinario pueden leer o mutar credenciales de automatización.
    // ----------------------------------------------------------------------------
    private static void MapIntegrationsEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings/integrations")
            .WithTags("Settings")
            .RequireAuthorization(IntegrationCredentialAdminPolicy);

        group.MapGet("/", async (IMediator m, CancellationToken ct) =>
                ToResult(await m.Send(new ListIntegrationCredentialsQuery(), ct)))
            .WithName("ListIntegrationCredentials");

        group.MapPost("/", async (
            [FromBody] CreateIntegrationCredentialRequest body,
            HttpContext http,
            IMediator m,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var cmd = new CreateIntegrationCredentialCommand(
                body.Name,
                body.Type,
                body.DisplayName,
                body.PlainValue,
                body.Metadata,
                body.Description);
            var r = await m.Send(cmd, ct);
            if (r.IsFailure)
            {
                return MapError(r.Error);
            }

            AuditCredentialMutation(http, loggerFactory, "created", r.Value.Id);
            return Results.Created($"/api/settings/integrations/{r.Value.Id}", r.Value);
        }).WithName("CreateIntegrationCredential");

        group.MapDelete("/{credentialId}", async (
            string credentialId,
            HttpContext http,
            IMediator m,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var r = await m.Send(new DeleteIntegrationCredentialCommand(credentialId), ct);
            if (r.IsFailure)
            {
                return MapError(r.Error);
            }

            AuditCredentialMutation(http, loggerFactory, "deleted", credentialId);
            return Results.NoContent();
        }).WithName("DeleteIntegrationCredential");

        group.MapPost("/{credentialId}/rotate", async (
            string credentialId,
            [FromBody] RotateIntegrationCredentialRequest body,
            HttpContext http,
            IMediator m,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var r = await m.Send(new RotateIntegrationCredentialCommand(credentialId, body.NewPlainValue), ct);
            if (r.IsFailure)
            {
                return MapError(r.Error);
            }

            AuditCredentialMutation(http, loggerFactory, "rotated", credentialId);
            return Results.NoContent();
        }).WithName("RotateIntegrationCredential");
    }

    // Scopes para domains/environments. Integrations usa su policy cookie+admin aparte.
    private const string ScopeSettingsRead = "scope:settings:read";
    private const string ScopeSettingsWrite = "scope:settings:write";

    // ----------------------------------------------------------------------------
    // Base domains. Son metadata (no secretos) pero impactan la config de proxy/TLS.
    // ----------------------------------------------------------------------------
    private static void MapDomainsEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings/domains")
            .WithTags("Settings");

        group.MapGet("/", async (IMediator m, CancellationToken ct) =>
                ToResult(await m.Send(new ListBaseDomainsQuery(), ct)))
            .RequireAuthorization(ScopeSettingsRead)
            .WithName("ListBaseDomains");

        group.MapPost("/", async ([FromBody] CreateBaseDomainRequest body, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new CreateBaseDomainCommand(body.Hostname, body.CloudflareZoneId), ct);
            return r.IsSuccess
                ? Results.Created($"/api/settings/domains/{r.Value.Id}", r.Value)
                : MapError(r.Error);
        })
        .RequireAuthorization(ScopeSettingsWrite)
        .WithName("CreateBaseDomain");

        group.MapPost("/{baseDomainId}/activate", async (string baseDomainId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new ActivateBaseDomainCommand(baseDomainId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeSettingsWrite)
        .WithName("ActivateBaseDomain");

        group.MapPost("/{baseDomainId}/wildcard-configured", async (string baseDomainId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new MarkWildcardConfiguredCommand(baseDomainId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeSettingsWrite)
        .WithName("MarkBaseDomainWildcardConfigured");

        group.MapDelete("/{baseDomainId}", async (string baseDomainId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new DeleteBaseDomainCommand(baseDomainId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeSettingsWrite)
        .WithName("DeleteBaseDomain");
    }

    // ----------------------------------------------------------------------------
    // Ambientes válidos.
    // ----------------------------------------------------------------------------
    private static void MapEnvironmentsEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings/environments")
            .WithTags("Settings");

        group.MapGet("/", async (IMediator m, CancellationToken ct) =>
                ToResult(await m.Send(new ListEnvironmentDefinitionsQuery(), ct)))
            .RequireAuthorization(ScopeSettingsRead)
            .WithName("ListEnvironmentDefinitions");

        group.MapPost("/", async ([FromBody] CreateEnvironmentDefinitionRequest body, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new CreateEnvironmentDefinitionCommand(body.Slug, body.DisplayName, body.Order), ct);
            return r.IsSuccess
                ? Results.Created($"/api/settings/environments/{r.Value.Id}", r.Value)
                : MapError(r.Error);
        })
        .RequireAuthorization(ScopeSettingsWrite)
        .WithName("CreateEnvironmentDefinition");

        group.MapPatch("/{environmentId}", async (string environmentId, [FromBody] UpdateEnvironmentRequest body, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new UpdateEnvironmentDefinitionCommand(environmentId, body.DisplayName), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : MapError(r.Error);
        })
        .RequireAuthorization(ScopeSettingsWrite)
        .WithName("UpdateEnvironmentDefinition");

        group.MapDelete("/{environmentId}", async (string environmentId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new DeleteEnvironmentDefinitionCommand(environmentId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeSettingsWrite)
        .WithName("DeleteEnvironmentDefinition");

        group.MapPost("/reorder", async ([FromBody] ReorderEnvironmentDefinitionsRequest body, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new ReorderEnvironmentDefinitionsCommand(body.Ids), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeSettingsWrite)
        .WithName("ReorderEnvironmentDefinitions");
    }

    // ---------------------- Request DTOs ----------------------

    public sealed record CreateIntegrationCredentialRequest(
        string Name,
        IntegrationCredentialType Type,
        string DisplayName,
        string PlainValue,
        IReadOnlyDictionary<string, string>? Metadata,
        string? Description);

    public sealed record RotateIntegrationCredentialRequest(string NewPlainValue);

    public sealed record CreateBaseDomainRequest(string Hostname, string? CloudflareZoneId);

    public sealed record CreateEnvironmentDefinitionRequest(string Slug, string DisplayName, int? Order);

    public sealed record UpdateEnvironmentRequest(string DisplayName);

    public sealed record ReorderEnvironmentDefinitionsRequest(IReadOnlyList<string> Ids);

    // ---------------------- Helpers ----------------------

    private static IResult ToResult<T>(Result<T> r)
        => r.IsSuccess ? Results.Ok(r.Value) : MapError(r.Error);

    private static void AuditCredentialMutation(
        HttpContext http,
        ILoggerFactory loggerFactory,
        string action,
        string credentialId)
    {
        var actorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.Identity?.Name
            ?? "unknown";
        loggerFactory.CreateLogger("Aethra.Settings.IntegrationCredentials.Audit")
            .LogInformation(
                "Integration credential {CredentialAction} by actor {ActorId}: {CredentialId}",
                action,
                actorId,
                credentialId);
    }

    private static IResult MapError(Error e) => e.Type switch
    {
        ErrorType.Validation => Results.UnprocessableEntity(new { e.Code, e.Message }),
        ErrorType.NotFound => Results.NotFound(new { e.Code, e.Message }),
        ErrorType.Conflict => Results.Conflict(new { e.Code, e.Message }),
        _ => Results.Problem(e.Message),
    };
}
