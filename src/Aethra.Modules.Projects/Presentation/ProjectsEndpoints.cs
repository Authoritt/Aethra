using Aethra.Modules.Projects.UseCases.Clients.Commands;
using Aethra.Modules.Projects.UseCases.Clients.Queries;
using Aethra.Modules.Projects.UseCases.Instances.Commands;
using Aethra.Modules.Projects.UseCases.Instances.Queries;
using Aethra.Modules.Projects.UseCases.Projects.Commands;
using Aethra.Modules.Projects.UseCases.Projects.Queries;
using Aethra.Modules.Projects.UseCases.Templates.Commands;
using Aethra.Modules.Projects.UseCases.Templates.Dtos;
using Aethra.Modules.Projects.UseCases.Templates.Queries;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Aethra.Modules.Projects.Presentation;

/// <summary>
/// Endpoints REST del módulo Projects (F9.5). Cada grupo (projects, templates, clients, instances)
/// se cablea contra los Commands/Queries en <c>UseCases/</c>. Todos requieren autenticación.
/// </summary>
public static class ProjectsEndpoints
{
    public static IEndpointRouteBuilder MapProjectsEndpoints(this IEndpointRouteBuilder app)
    {
        MapProjects(app);
        MapTemplates(app);
        MapClients(app);
        MapInstances(app);
        return app;
    }

    // -------------------------------------------------------------------------
    // Projects
    // -------------------------------------------------------------------------
    // Scope policies. Templates/Clients/Instances forman parte del dominio Projects —
    // usamos el mismo par de scopes para todo (consistente con McpScopes.ProjectsRead/Write).
    private const string ScopeProjectsRead = "scope:projects:read";
    private const string ScopeProjectsWrite = "scope:projects:write";

    private static void MapProjects(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects").WithTags("Projects");

        group.MapGet("/", async (IMediator m, CancellationToken ct) =>
                ToResult(await m.Send(new ListProjectsQuery(), ct)))
            .RequireAuthorization(ScopeProjectsRead)
            .WithName("ListProjects");

        group.MapPost("/", async ([FromBody] CreateProjectRequest body, IMediator m, CancellationToken ct) =>
        {
            var cmd = new CreateProjectCommand(body.Slug, body.Name, body.Description, body.Color, body.Icon);
            var r = await m.Send(cmd, ct);
            return r.IsSuccess
                ? Results.Created($"/api/projects/{r.Value.id}", r.Value)
                : MapError(r.Error);
        })
        .RequireAuthorization(ScopeProjectsWrite)
        .WithName("CreateProject");

        group.MapGet("/{id}", async (string id, IMediator m, CancellationToken ct) =>
                ToResult(await m.Send(new GetProjectByIdQuery(id), ct)))
            .RequireAuthorization(ScopeProjectsRead)
            .WithName("GetProject");
    }

    // -------------------------------------------------------------------------
    // Templates (anidados bajo projects para list/create; flat para get/{id}).
    // -------------------------------------------------------------------------
    private static void MapTemplates(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/projects/{projectId}/templates",
                async (string projectId, IMediator m, CancellationToken ct) =>
                    ToResult(await m.Send(new ListTemplatesQuery(projectId), ct)))
            .WithTags("Templates").RequireAuthorization(ScopeProjectsRead).WithName("ListTemplates");

        app.MapPost("/api/projects/{projectId}/templates", async (
            string projectId,
            [FromBody] CreateTemplateRequest body,
            IMediator m,
            CancellationToken ct) =>
        {
            var cmd = new CreateTemplateCommand(
                ProjectId: projectId,
                Slug: body.Slug,
                Name: body.Name,
                Description: body.Description,
                GitRepoUrl: body.GitRepoUrl,
                Branch: body.Branch,
                BaseDirectory: body.BaseDirectory,
                WatchPaths: body.WatchPaths,
                AccessTokenCredentialName: body.AccessTokenCredentialName,
                BuildType: body.BuildType,
                DockerfilePath: body.DockerfilePath,
                ComposeFilePath: body.ComposeFilePath,
                BuildArgs: body.BuildArgs,
                WebhookSecret: body.WebhookSecret);
            var r = await m.Send(cmd, ct);
            return r.IsSuccess
                ? Results.Created($"/api/templates/{r.Value.id}", r.Value)
                : MapError(r.Error);
        }).WithTags("Templates").RequireAuthorization(ScopeProjectsWrite).WithName("CreateTemplate");

        app.MapGet("/api/templates/{id}", async (string id, IMediator m, CancellationToken ct) =>
                ToResult(await m.Send(new GetTemplateByIdQuery(id), ct)))
            .WithTags("Templates").RequireAuthorization(ScopeProjectsRead).WithName("GetTemplate");

        // F11.2: inspecciona un repo Git (shallow clone) y devuelve qué BuildType usar.
        app.MapPost("/api/templates/discover", async (
            [FromBody] DiscoverTemplateRequest body,
            IMediator m,
            CancellationToken ct) =>
        {
            var query = new DiscoverTemplateQuery(body.GitRepoUrl, body.Branch);
            var r = await m.Send(query, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : MapError(r.Error);
        }).WithTags("Templates").RequireAuthorization(ScopeProjectsRead).WithName("DiscoverTemplate");
    }

    // -------------------------------------------------------------------------
    // Clients
    // -------------------------------------------------------------------------
    private static void MapClients(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/projects/{projectId}/clients",
                async (string projectId, IMediator m, CancellationToken ct) =>
                    ToResult(await m.Send(new ListClientsQuery(projectId), ct)))
            .WithTags("Clients").RequireAuthorization(ScopeProjectsRead).WithName("ListClients");

        app.MapPost("/api/projects/{projectId}/clients", async (
            string projectId,
            [FromBody] CreateClientRequest body,
            IMediator m,
            CancellationToken ct) =>
        {
            var cmd = new CreateClientCommand(
                ProjectId: projectId,
                Slug: body.Slug,
                DisplayName: body.DisplayName,
                Description: body.Description,
                ContactEmail: body.ContactEmail,
                BillingTag: body.BillingTag);
            var r = await m.Send(cmd, ct);
            return r.IsSuccess
                ? Results.Created($"/api/clients/{r.Value.id}", r.Value)
                : MapError(r.Error);
        }).WithTags("Clients").RequireAuthorization(ScopeProjectsWrite).WithName("CreateClient");

        app.MapGet("/api/clients/{id}", async (string id, IMediator m, CancellationToken ct) =>
                ToResult(await m.Send(new GetClientByIdQuery(id), ct)))
            .WithTags("Clients").RequireAuthorization(ScopeProjectsRead).WithName("GetClient");
    }

    // -------------------------------------------------------------------------
    // Instances
    // -------------------------------------------------------------------------
    private static void MapInstances(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/templates/{templateId}/instances",
                async (string templateId, IMediator m, CancellationToken ct) =>
                    ToResult(await m.Send(new ListInstancesQuery(templateId), ct)))
            .WithTags("Instances").RequireAuthorization(ScopeProjectsRead).WithName("ListInstances");

        app.MapPost("/api/templates/{templateId}/instances", async (
            string templateId,
            [FromBody] CreateInstanceRequest body,
            IMediator m,
            CancellationToken ct) =>
        {
            var cmd = new CreateInstanceCommand(
                TemplateId: templateId,
                ClientId: body.ClientId,
                Environment: body.Environment,
                TargetVmId: body.TargetVmId,
                SlugOverride: body.SlugOverride,
                Ports: body.Ports,
                Volumes: body.Volumes,
                Healthcheck: body.Healthcheck,
                AutoDeployOnNewBuild: body.AutoDeployOnNewBuild);
            var r = await m.Send(cmd, ct);
            return r.IsSuccess
                ? Results.Created($"/api/instances/{r.Value.id}", r.Value)
                : MapError(r.Error);
        }).WithTags("Instances").RequireAuthorization(ScopeProjectsWrite).WithName("CreateInstance");

        app.MapGet("/api/instances/{id}", async (string id, IMediator m, CancellationToken ct) =>
                ToResult(await m.Send(new GetInstanceByIdQuery(id), ct)))
            .WithTags("Instances").RequireAuthorization(ScopeProjectsRead).WithName("GetInstance");

        app.MapPost("/api/instances/{id}/custom-domain", async (
            string id,
            [FromBody] SetCustomDomainRequest body,
            IMediator m,
            CancellationToken ct) =>
        {
            var r = await m.Send(new SetCustomDomainCommand(id, body.CustomDomain), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithTags("Instances").RequireAuthorization(ScopeProjectsWrite).WithName("SetInstanceCustomDomain");
    }

    // -------------------------------------------------------------------------
    // Request DTOs (camelCase ya en los DTOs internos; ASP.NET hace case-insensitive bind
    // por defecto, así que aceptamos PascalCase en el wire también).
    // -------------------------------------------------------------------------

    public sealed record CreateProjectRequest(
        string Slug,
        string Name,
        string? Description,
        string? Color,
        string? Icon);

    public sealed record CreateTemplateRequest(
        string Slug,
        string Name,
        string? Description,
        string GitRepoUrl,
        string Branch,
        string? BaseDirectory,
        IReadOnlyList<string>? WatchPaths,
        string? AccessTokenCredentialName,
        string BuildType,
        string? DockerfilePath,
        string? ComposeFilePath,
        IReadOnlyList<TemplateBuildArgDto>? BuildArgs,
        string? WebhookSecret);

    public sealed record CreateClientRequest(
        string Slug,
        string DisplayName,
        string? Description,
        string? ContactEmail,
        string? BillingTag);

    public sealed record CreateInstanceRequest(
        string ClientId,
        string Environment,
        string TargetVmId,
        string? SlugOverride,
        IReadOnlyList<CreateInstancePortDto>? Ports,
        IReadOnlyList<CreateInstanceVolumeDto>? Volumes,
        CreateInstanceHealthcheckDto? Healthcheck,
        bool AutoDeployOnNewBuild);

    public sealed record SetCustomDomainRequest(string? CustomDomain);

    /// <summary>F11.2 — Body de <c>POST /api/templates/discover</c>.</summary>
    public sealed record DiscoverTemplateRequest(string GitRepoUrl, string? Branch);

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

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
