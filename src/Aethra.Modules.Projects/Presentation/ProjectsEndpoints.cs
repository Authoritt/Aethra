using Aethra.Modules.Projects.UseCases.Clients.Commands;
using Aethra.Modules.Projects.UseCases.Clients.Queries;
using Aethra.Modules.Projects.UseCases.EnvVars.Commands;
using Aethra.Modules.Projects.UseCases.EnvVars.Queries;
using Aethra.Modules.Projects.UseCases.Instances.Commands;
using Aethra.Modules.Projects.UseCases.Instances.Dtos;
using Aethra.Modules.Projects.UseCases.Instances.Queries;
using Aethra.Modules.Projects.UseCases.Projects.Commands;
using Aethra.Modules.Projects.UseCases.Projects.Queries;
using Aethra.Modules.Projects.UseCases.Secrets.Commands;
using Aethra.Modules.Projects.UseCases.Secrets.Queries;
using Aethra.Modules.Projects.UseCases.Templates.Commands;
using Aethra.Modules.Projects.UseCases.Templates.Dtos;
using Aethra.Modules.Projects.UseCases.Templates.Queries;
using Aethra.Shared.Contracts.Projects;
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
        MapEnvVarsAndSecrets(app);
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

        // F12.3 — actualizar el cap de previews concurrentes del Project.
        group.MapPatch("/{id}/preview-config", async (
            string id,
            [FromBody] SetPreviewConfigRequest body,
            IMediator m,
            CancellationToken ct) =>
        {
            var r = await m.Send(new SetPreviewConfigCommand(id, body.PreviewMaxConcurrent), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).RequireAuthorization(ScopeProjectsWrite).WithName("SetProjectPreviewConfig");

        // Editar nombre y apariencia del proyecto (el slug no cambia).
        group.MapPatch("/{id}", async (
            string id, [FromBody] UpdateProjectRequest body, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new UpdateProjectCommand(id, body.Name, body.Description, body.Color, body.Icon), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).RequireAuthorization(ScopeProjectsWrite).WithName("UpdateProject");

        // Borra el proyecto en cascada (templates, clients, instancias, env vars, secrets).
        // Si tiene instancias desplegadas requiere ?force=true (no detiene contenedores ni
        // limpia rutas del proxy — eso se hace aparte).
        group.MapDelete("/{id}", async (string id, bool? force, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new DeleteProjectCommand(id, force ?? false), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).RequireAuthorization(ScopeProjectsWrite).WithName("DeleteProject");
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

        // Editar plantilla (name/desc/source/build). El slug no cambia.
        app.MapPatch("/api/templates/{id}", async (
            string id, [FromBody] UpdateTemplateRequest body, IMediator m, CancellationToken ct) =>
        {
            var cmd = new UpdateTemplateCommand(
                id, body.Name, body.Description, body.GitRepoUrl, body.Branch, body.BaseDirectory,
                body.WatchPaths, body.AccessTokenCredentialName, body.BuildType, body.DockerfilePath,
                body.ComposeFilePath, body.BuildArgs);
            var r = await m.Send(cmd, ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithTags("Templates").RequireAuthorization(ScopeProjectsWrite).WithName("UpdateTemplate");

        // Borrar plantilla (force = cascada de instancias).
        app.MapDelete("/api/templates/{id}", async (
            string id, [FromQuery] bool? force, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new DeleteTemplateCommand(id, force ?? false), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithTags("Templates").RequireAuthorization(ScopeProjectsWrite).WithName("DeleteTemplate");

        // Rotar webhook secret (devuelve el nuevo en plain una vez).
        app.MapPost("/api/templates/{id}/rotate-webhook-secret", async (string id, IMediator m, CancellationToken ct) =>
            ToResult(await m.Send(new RotateWebhookSecretCommand(id), ct)))
            .WithTags("Templates").RequireAuthorization(ScopeProjectsWrite).WithName("RotateTemplateWebhookSecret");

        // F13 — define la topología de servicios multi-contenedor del template (deploy nativo).
        app.MapPut("/api/templates/{id}/services", async (
            string id,
            [FromBody] SetTemplateServicesRequest body,
            IMediator m,
            CancellationToken ct) =>
        {
            var cmd = new SetTemplateServicesCommand(
                id,
                (body.Services ?? [])
                    .Select(s => new TemplateServiceInput(
                        s.Name, s.Image, s.Port, s.PathPrefixes, s.Env, s.BuildMode, s.DockerfilePath,
                        s.Volumes?.Select(v => new TemplateVolumeInput(v.Name, v.ContainerPath, v.ReadOnly)).ToList(),
                        s.Hostname, s.BuildContext))
                    .ToList());
            var r = await m.Send(cmd, ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithTags("Templates").RequireAuthorization(ScopeProjectsWrite).WithName("SetTemplateServices");

        // F12.3 — Branch-per-Instance: reemplazar mapping Environment→Branch del Template.
        app.MapPatch("/api/templates/{id}/environment-mapping", async (
            string id,
            [FromBody] SetEnvironmentMappingRequest body,
            IMediator m,
            CancellationToken ct) =>
        {
            var items = (body.Mappings ?? [])
                .Select(x => new EnvironmentMappingItemDto(x.environment, x.branch))
                .ToList();
            var r = await m.Send(new SetEnvironmentMappingCommand(id, items), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithTags("Templates").RequireAuthorization(ScopeProjectsWrite).WithName("SetEnvironmentMapping");

        // F12.3 — Preview deployments: opt-in / opt-out del auto-create de Instances ephemerals.
        app.MapPatch("/api/templates/{id}/auto-preview", async (
            string id,
            [FromBody] SetAutoPreviewRequest body,
            IMediator m,
            CancellationToken ct) =>
        {
            var r = await m.Send(new SetAutoPreviewCommand(id, body.Enabled), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithTags("Templates").RequireAuthorization(ScopeProjectsWrite).WithName("SetAutoPreviewPullRequests");

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

        // Instancias asociadas a un Client (lo usa el detalle del cliente).
        app.MapGet("/api/clients/{id}/instances", async (string id, IMediator m, CancellationToken ct) =>
                ToResult(await m.Send(new ListInstancesQuery(ClientId: id), ct)))
            .WithTags("Clients").RequireAuthorization(ScopeProjectsRead).WithName("ListClientInstances");

        // Editar info administrativa del client (display name/desc/email/billing). El slug no cambia.
        app.MapPatch("/api/clients/{id}", async (
            string id, [FromBody] UpdateClientRequest body, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new UpdateClientCommand(id, body.DisplayName, body.Description, body.ContactEmail, body.BillingTag), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithTags("Clients").RequireAuthorization(ScopeProjectsWrite).WithName("UpdateClient");

        // Borrar client (force = cascada de instancias asociadas).
        app.MapDelete("/api/clients/{id}", async (
            string id, [FromQuery] bool? force, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new DeleteClientCommand(id, force ?? false), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithTags("Clients").RequireAuthorization(ScopeProjectsWrite).WithName("DeleteClient");
    }

    // -------------------------------------------------------------------------
    // Instances
    // -------------------------------------------------------------------------
    private static void MapInstances(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/templates/{templateId}/instances",
                async (string templateId, IMediator m, CancellationToken ct) =>
                    ToResult(await m.Send(new ListInstancesQuery(TemplateId: templateId), ct)))
            .WithTags("Instances").RequireAuthorization(ScopeProjectsRead).WithName("ListInstances");

        // F12.3 — listado plano con filtros para "Mis previews" / "Ephemerals del Project".
        app.MapGet("/api/instances", async (
            [FromQuery] string? projectId,
            [FromQuery] string? templateId,
            [FromQuery(Name = "owner_id")] string? ownerId,
            [FromQuery] bool? ephemeral,
            HttpContext http,
            IMediator m,
            CancellationToken ct) =>
        {
            // owner_id=me → reemplaza por el userId del cookie.
            if (string.Equals(ownerId, "me", StringComparison.OrdinalIgnoreCase))
            {
                ownerId = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            }
            return ToResult(await m.Send(new ListInstancesQuery(
                TemplateId: templateId,
                ProjectId: projectId,
                OwnerUserId: ownerId,
                IsEphemeral: ephemeral), ct));
        })
        .WithTags("Instances").RequireAuthorization(ScopeProjectsRead).WithName("ListInstancesFiltered");

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
                AutoDeployOnNewBuild: body.AutoDeployOnNewBuild,
                TrackedRef: body.TrackedRef);
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

        // F12.3 — setear/limpiar el TrackedRef de una Instance (override de la cascada del Template).
        app.MapPatch("/api/instances/{id}/tracked-ref", async (
            string id,
            [FromBody] SetTrackedRefRequest body,
            IMediator m,
            CancellationToken ct) =>
        {
            var r = await m.Send(new SetTrackedRefCommand(id, body.TrackedRef), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithTags("Instances").RequireAuthorization(ScopeProjectsWrite).WithName("SetInstanceTrackedRef");

        // F12.3 — borrar Instance ephemeral (cleanup manual de preview).
        app.MapDelete("/api/instances/{id}", async (
            string id,
            [FromQuery] bool? force,
            IMediator m,
            CancellationToken ct) =>
        {
            var r = await m.Send(new DeleteInstanceCommand(id, force ?? false), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithTags("Instances").RequireAuthorization(ScopeProjectsWrite).WithName("DeleteInstance");

        // Reconfigurar runtime de una Instance (ports/volumes/healthcheck/targetVm/autoDeploy).
        app.MapPatch("/api/instances/{id}", async (
            string id, [FromBody] ReconfigureInstanceRequest body, IMediator m, CancellationToken ct) =>
        {
            var cmd = new ReconfigureInstanceCommand(id, body.TargetVmId, body.Ports, body.Volumes, body.Healthcheck, body.AutoDeployOnNewBuild);
            var r = await m.Send(cmd, ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithTags("Instances").RequireAuthorization(ScopeProjectsWrite).WithName("ReconfigureInstance");

        // Toggle auto-deploy on new build (consumido por el AutoDeployToggle de la UI).
        app.MapPost("/api/instances/{id}/auto-deploy/enable", async (string id, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new SetAutoDeployCommand(id, true), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithTags("Instances").RequireAuthorization(ScopeProjectsWrite).WithName("EnableInstanceAutoDeploy");

        app.MapPost("/api/instances/{id}/auto-deploy/disable", async (string id, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new SetAutoDeployCommand(id, false), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithTags("Instances").RequireAuthorization(ScopeProjectsWrite).WithName("DisableInstanceAutoDeploy");
    }

    // -------------------------------------------------------------------------
    // Env vars & Secrets (scope-genérico: project|template|client|instance).
    // El scope se direcciona por query-string (?scopeType=&scopeId=), igual semántica que el
    // writer cross-module y el endpoint instance-only PUT /api/instances/{id}/env-vars.
    // -------------------------------------------------------------------------
    private static void MapEnvVarsAndSecrets(IEndpointRouteBuilder app)
    {
        // ---- Env vars ----
        app.MapGet("/api/env-vars", async (
                [FromQuery] string scopeType,
                [FromQuery] string scopeId,
                IMediator m,
                CancellationToken ct) =>
            ToResult(await m.Send(new ListEnvVarsQuery(scopeType, scopeId), ct)))
            .WithTags("EnvVars").RequireAuthorization(ScopeProjectsRead).WithName("ListEnvVars");

        app.MapPut("/api/env-vars", async (
            [FromQuery] string scopeType,
            [FromQuery] string scopeId,
            [FromBody] SetEnvVarsRequest body,
            IEnvVarWriter writer,
            CancellationToken ct) =>
        {
            if (!TryParseEnvScope(scopeType, out var scope))
            {
                return MapError(InvalidScopeError(scopeType));
            }
            if (string.IsNullOrWhiteSpace(scopeId))
            {
                return MapError(Error.Validation("env_scope.missing_id", "scopeId es obligatorio."));
            }
            var items = body?.Vars ?? [];
            var upserts = items
                .Where(v => !string.IsNullOrWhiteSpace(v.Key))
                .Select(v => new EnvVarUpsert(v.Key.Trim(), v.Value ?? string.Empty, v.IsBuildTime, v.IsRuntime ?? true))
                .ToList();
            if (upserts.Count == 0)
            {
                return MapError(Error.Validation("env_vars.empty", "vars no puede estar vacío."));
            }
            try
            {
                await writer.UpsertManyAsync(scope, scopeId, "manual:api", upserts, ct);
            }
            catch (ArgumentException ex)
            {
                return MapError(Error.Validation("env_vars.invalid", ex.Message));
            }
            return Results.Ok(new { scopeType = scope.ToString(), scopeId, count = upserts.Count, source = "manual:api" });
        }).WithTags("EnvVars").RequireAuthorization(ScopeProjectsWrite).WithName("SetScopedEnvVars");

        app.MapDelete("/api/env-vars", async (
            [FromQuery] string scopeType,
            [FromQuery] string scopeId,
            [FromQuery] string key,
            IMediator m,
            CancellationToken ct) =>
        {
            var r = await m.Send(new DeleteEnvVarCommand(scopeType, scopeId, key), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithTags("EnvVars").RequireAuthorization(ScopeProjectsWrite).WithName("DeleteScopedEnvVar");

        // ---- Secrets ----
        // GET nunca devuelve valores ni ciphers (solo metadata + hasValue).
        app.MapGet("/api/secrets", async (
                [FromQuery] string scopeType,
                [FromQuery] string scopeId,
                IMediator m,
                CancellationToken ct) =>
            ToResult(await m.Send(new ListSecretsQuery(scopeType, scopeId), ct)))
            .WithTags("Secrets").RequireAuthorization(ScopeProjectsRead).WithName("ListSecrets");

        app.MapPut("/api/secrets", async (
            [FromQuery] string scopeType,
            [FromQuery] string scopeId,
            [FromBody] SetSecretsRequest body,
            ISecretWriter writer,
            CancellationToken ct) =>
        {
            if (!TryParseEnvScope(scopeType, out var scope))
            {
                return MapError(InvalidScopeError(scopeType));
            }
            if (string.IsNullOrWhiteSpace(scopeId))
            {
                return MapError(Error.Validation("env_scope.missing_id", "scopeId es obligatorio."));
            }
            var items = body?.Secrets ?? [];
            var upserts = items
                .Where(s => !string.IsNullOrWhiteSpace(s.Key))
                .Select(s => new SecretUpsert(s.Key.Trim(), s.Value ?? string.Empty))
                .ToList();
            if (upserts.Count == 0)
            {
                return MapError(Error.Validation("secrets.empty", "secrets no puede estar vacío."));
            }
            try
            {
                await writer.UpsertManyAsync(scope, scopeId, "manual:api", upserts, ct);
            }
            catch (ArgumentException ex)
            {
                return MapError(Error.Validation("secrets.invalid", ex.Message));
            }
            return Results.Ok(new { scopeType = scope.ToString(), scopeId, count = upserts.Count, source = "manual:api" });
        }).WithTags("Secrets").RequireAuthorization(ScopeProjectsWrite).WithName("SetScopedSecrets");

        app.MapDelete("/api/secrets", async (
            [FromQuery] string scopeType,
            [FromQuery] string scopeId,
            [FromQuery] string key,
            IMediator m,
            CancellationToken ct) =>
        {
            var r = await m.Send(new DeleteSecretCommand(scopeType, scopeId, key), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithTags("Secrets").RequireAuthorization(ScopeProjectsWrite).WithName("DeleteScopedSecret");
    }

    /// <summary>
    /// Traduce el discriminador textual de scope (<c>project|template|client|instance</c>) al enum
    /// cross-module <see cref="EnvVarScope"/> que consumen los writers.
    /// </summary>
    private static bool TryParseEnvScope(string? scopeType, out EnvVarScope scope)
        => Enum.TryParse(scopeType, ignoreCase: true, out scope) && Enum.IsDefined(scope);

    private static Error InvalidScopeError(string? scopeType)
        => Error.Validation("env_scope.invalid",
            $"scopeType='{scopeType}' inválido. Use project, template, client o instance.");

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

    public sealed record UpdateTemplateRequest(
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
        IReadOnlyList<TemplateBuildArgDto>? BuildArgs);

    public sealed record ReconfigureInstanceRequest(
        string? TargetVmId,
        IReadOnlyList<CreateInstancePortDto>? Ports,
        IReadOnlyList<CreateInstanceVolumeDto>? Volumes,
        CreateInstanceHealthcheckDto? Healthcheck,
        bool? AutoDeployOnNewBuild);

    public sealed record SetTemplateServicesRequest(IReadOnlyList<SetTemplateServiceItem>? Services);

    public sealed record SetTemplateServiceItem(
        string Name,
        string Image,
        int Port,
        IReadOnlyList<string>? PathPrefixes,
        IReadOnlyDictionary<string, string>? Env,
        string? BuildMode = null,
        string? DockerfilePath = null,
        IReadOnlyList<SetTemplateVolumeItem>? Volumes = null,
        string? Hostname = null,
        string? BuildContext = null);

    public sealed record SetTemplateVolumeItem(
        string Name,
        string ContainerPath,
        bool ReadOnly = false);

    public sealed record CreateClientRequest(
        string Slug,
        string DisplayName,
        string? Description,
        string? ContactEmail,
        string? BillingTag);

    /// <summary>Body para <c>PATCH /api/clients/{id}</c> (el slug no cambia).</summary>
    public sealed record UpdateClientRequest(
        string DisplayName,
        string? Description,
        string? ContactEmail,
        string? BillingTag);

    /// <summary>Body para <c>PATCH /api/projects/{id}</c> (el slug no cambia).</summary>
    public sealed record UpdateProjectRequest(
        string Name,
        string? Description,
        string? Color,
        string? Icon);

    public sealed record CreateInstanceRequest(
        string ClientId,
        string Environment,
        string TargetVmId,
        string? SlugOverride,
        IReadOnlyList<CreateInstancePortDto>? Ports,
        IReadOnlyList<CreateInstanceVolumeDto>? Volumes,
        CreateInstanceHealthcheckDto? Healthcheck,
        bool AutoDeployOnNewBuild,
        string? TrackedRef = null);

    public sealed record SetCustomDomainRequest(string? CustomDomain);

    /// <summary>F11.2 — Body de <c>POST /api/templates/discover</c>.</summary>
    public sealed record DiscoverTemplateRequest(string GitRepoUrl, string? Branch);

    /// <summary>F12.3 — Body para <c>PATCH /api/templates/{id}/environment-mapping</c>.</summary>
    public sealed record SetEnvironmentMappingRequest(IReadOnlyList<EnvironmentMappingRow>? Mappings);
    public sealed record EnvironmentMappingRow(string environment, string branch);

    /// <summary>F12.3 — Body para <c>PATCH /api/templates/{id}/auto-preview</c>.</summary>
    public sealed record SetAutoPreviewRequest(bool Enabled);

    /// <summary>F12.3 — Body para <c>PATCH /api/projects/{id}/preview-config</c>.</summary>
    public sealed record SetPreviewConfigRequest(int PreviewMaxConcurrent);

    /// <summary>F12.3 — Body para <c>PATCH /api/instances/{id}/tracked-ref</c>.</summary>
    public sealed record SetTrackedRefRequest(string? TrackedRef);

    /// <summary>Body para <c>PUT /api/env-vars</c> (upsert idempotente por scope).</summary>
    public sealed record SetEnvVarsRequest(IReadOnlyList<SetEnvVarItem>? Vars);

    public sealed record SetEnvVarItem(
        string Key,
        string? Value,
        bool IsBuildTime = false,
        bool? IsRuntime = true);

    /// <summary>Body para <c>PUT /api/secrets</c>. El valor se cifra antes de persistir.</summary>
    public sealed record SetSecretsRequest(IReadOnlyList<SetSecretItem>? Secrets);

    public sealed record SetSecretItem(string Key, string? Value);

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
