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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Aethra.Modules.Settings.Presentation;

public static class SettingsEndpoints
{
    /// <summary>
    /// Constante duplicada (NO referenciada) de <c>Aethra.Modules.Identity.Infrastructure.Authentication.ApiKeyAuthSchemes.CookieScheme</c>
    /// para no acoplar este módulo a internals de Identity. El host (apps/api) registra
    /// el cookie scheme con este mismo nombre en <c>AuthSchemes.Cookie</c>.
    /// </summary>
    private const string CookieScheme = "aethra.cookie";

    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        MapIntegrationsEndpoints(app);
        MapDomainsEndpoints(app);
        MapEnvironmentsEndpoints(app);
        return app;
    }

    // ----------------------------------------------------------------------------
    // Integraciones (credenciales externas). SOLO cookie auth — una API key no debe
    // poder leer/crear credenciales que la API key misma podría usar.
    // ----------------------------------------------------------------------------
    private static void MapIntegrationsEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings/integrations")
            .WithTags("Settings")
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = CookieScheme,
            });

        group.MapGet("/", async (IMediator m, CancellationToken ct) =>
                ToResult(await m.Send(new ListIntegrationCredentialsQuery(), ct)))
            .WithName("ListIntegrationCredentials");

        group.MapPost("/", async ([FromBody] CreateIntegrationCredentialRequest body, IMediator m, CancellationToken ct) =>
        {
            var cmd = new CreateIntegrationCredentialCommand(
                body.Name,
                body.Type,
                body.DisplayName,
                body.PlainValue,
                body.Metadata,
                body.Description);
            var r = await m.Send(cmd, ct);
            return r.IsSuccess
                ? Results.Created($"/api/settings/integrations/{r.Value.Id}", r.Value)
                : MapError(r.Error);
        }).WithName("CreateIntegrationCredential");

        group.MapDelete("/{credentialId}", async (string credentialId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new DeleteIntegrationCredentialCommand(credentialId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithName("DeleteIntegrationCredential");

        group.MapPost("/{credentialId}/rotate", async (
            string credentialId,
            [FromBody] RotateIntegrationCredentialRequest body,
            IMediator m,
            CancellationToken ct) =>
        {
            var r = await m.Send(new RotateIntegrationCredentialCommand(credentialId, body.NewPlainValue), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithName("RotateIntegrationCredential");
    }

    // ----------------------------------------------------------------------------
    // Base domains. Auth policy default (cookie o api key) — son metadata, no secretos.
    // ----------------------------------------------------------------------------
    private static void MapDomainsEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings/domains")
            .WithTags("Settings")
            .RequireAuthorization();

        group.MapGet("/", async (IMediator m, CancellationToken ct) =>
                ToResult(await m.Send(new ListBaseDomainsQuery(), ct)))
            .WithName("ListBaseDomains");

        group.MapPost("/", async ([FromBody] CreateBaseDomainRequest body, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new CreateBaseDomainCommand(body.Hostname, body.CloudflareZoneId), ct);
            return r.IsSuccess
                ? Results.Created($"/api/settings/domains/{r.Value.Id}", r.Value)
                : MapError(r.Error);
        }).WithName("CreateBaseDomain");

        group.MapPost("/{baseDomainId}/activate", async (string baseDomainId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new ActivateBaseDomainCommand(baseDomainId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithName("ActivateBaseDomain");

        group.MapPost("/{baseDomainId}/wildcard-configured", async (string baseDomainId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new MarkWildcardConfiguredCommand(baseDomainId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithName("MarkBaseDomainWildcardConfigured");

        group.MapDelete("/{baseDomainId}", async (string baseDomainId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new DeleteBaseDomainCommand(baseDomainId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithName("DeleteBaseDomain");
    }

    // ----------------------------------------------------------------------------
    // Ambientes válidos.
    // ----------------------------------------------------------------------------
    private static void MapEnvironmentsEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings/environments")
            .WithTags("Settings")
            .RequireAuthorization();

        group.MapGet("/", async (IMediator m, CancellationToken ct) =>
                ToResult(await m.Send(new ListEnvironmentDefinitionsQuery(), ct)))
            .WithName("ListEnvironmentDefinitions");

        group.MapPost("/", async ([FromBody] CreateEnvironmentDefinitionRequest body, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new CreateEnvironmentDefinitionCommand(body.Slug, body.DisplayName, body.Order), ct);
            return r.IsSuccess
                ? Results.Created($"/api/settings/environments/{r.Value.Id}", r.Value)
                : MapError(r.Error);
        }).WithName("CreateEnvironmentDefinition");

        group.MapDelete("/{environmentId}", async (string environmentId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new DeleteEnvironmentDefinitionCommand(environmentId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithName("DeleteEnvironmentDefinition");

        group.MapPost("/reorder", async ([FromBody] ReorderEnvironmentDefinitionsRequest body, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new ReorderEnvironmentDefinitionsCommand(body.Ids), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithName("ReorderEnvironmentDefinitions");
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

    public sealed record ReorderEnvironmentDefinitionsRequest(IReadOnlyList<string> Ids);

    // ---------------------- Helpers ----------------------

    private static IResult ToResult<T>(Result<T> r)
        => r.IsSuccess ? Results.Ok(r.Value) : MapError(r.Error);

    private static IResult MapError(Error e) => e.Type switch
    {
        ErrorType.Validation => Results.UnprocessableEntity(new { e.Code, e.Message }),
        ErrorType.NotFound => Results.NotFound(new { e.Code, e.Message }),
        ErrorType.Conflict => Results.Conflict(new { e.Code, e.Message }),
        _ => Results.Problem(e.Message),
    };
}
