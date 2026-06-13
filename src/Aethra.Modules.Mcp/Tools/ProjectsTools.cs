using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Projects.UseCases.Clients.Commands;
using Aethra.Modules.Projects.UseCases.Instances.Commands;
using Aethra.Modules.Projects.UseCases.Instances.Queries;
using Aethra.Modules.Projects.UseCases.Projects.Commands;
using Aethra.Modules.Projects.UseCases.Templates.Commands;
using Aethra.Modules.Projects.UseCases.Projects.Queries;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

/// <summary>
/// Herramientas para gestionar Projects/Templates/Clients/Instances. En F9.0 el módulo Projects
/// está sin UseCases (en proceso de A1); estas tools quedan stubeadas devolviendo
/// <c>not_implemented_post_refactor</c> hasta que F9.5 las reescriba.
/// </summary>
[McpServerToolType]
public sealed class ProjectsTools(IMediator mediator, IMcpCallerContext caller)
{
    [McpServerTool(Name = "aethra_list_projects", ReadOnly = true, OpenWorld = false)]
    [Description("Lista todos los proyectos con sus contadores de templates y clients. Read-only.")]
    public async Task<object> ListAsync(CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsRead);
        }
        var result = await mediator.Send(new ListProjectsQuery(), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_get_project", ReadOnly = true, OpenWorld = false)]
    [Description("Detalle de un proyecto: slug, nombre, descripción, color, icon, contadores de templates/clients "
        + "y timestamps. Read-only.")]
    public async Task<object> GetAsync(
        [Description("ID del proyecto (formato 'prj_...').")] string projectId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsRead);
        }
        var result = await mediator.Send(new GetProjectByIdQuery(projectId), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_create_project", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Crea un proyecto: el contenedor de nivel superior que agrupa templates, clients e instances. "
        + "Devuelve el detalle del proyecto creado.")]
    public async Task<object> CreateAsync(
        [Description("Slug único (lowercase, a-z 0-9 -).")] string slug,
        [Description("Nombre display human-readable.")] string name,
        [Description("Descripción opcional.")] string? description,
        [Description("Color hex opcional para la UI (ej. '#22c55e').")] string? color,
        [Description("Icon opcional (nombre/emoji para la UI).")] string? icon,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        var result = await mediator
            .Send(new CreateProjectCommand(slug, name, description, color, icon), ct)
            .ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_delete_project", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("CRITICAL: borra un proyecto y TODO su árbol en CASCADA (instancias desplegadas, templates, clients "
        + "y sus env vars/secrets) — hard delete, no recuperable. Si el proyecto tiene instancias desplegadas, FALLA "
        + "salvo que pases force=true para confirmar la cascada. Usá dry_run=true primero para ver el plan.")]
    public async Task<object> DeleteAsync(
        [Description("ID del proyecto (formato 'prj_...').")] string projectId,
        [Description("Si true, confirma el borrado en CASCADA aunque haya instancias desplegadas.")] bool force,
        [Description("Si true, NO borra — devuelve el plan.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        if (dryRun)
        {
            return McpResponses.DryRun(
                wouldCall: $"DELETE /api/projects/{projectId}{(force ? "?force=true" : string.Empty)}",
                plan: new
                {
                    projectId,
                    force,
                    action = "CASCADE hard-delete: project + templates + clients + instances + their env vars/secrets",
                    note = "Sin force=true, falla si el proyecto tiene instancias desplegadas.",
                });
        }
        var result = await mediator.Send(new DeleteProjectCommand(projectId, force), ct).ConfigureAwait(false);
        return result.IsSuccess
            ? McpResponses.Ok(new { project_id = projectId, deleted = true, cascade = force })
            : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_create_client", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Crea un client (tenant) dentro de un proyecto. El slug es único por proyecto. "
        + "Devuelve el detalle del client.")]
    public async Task<object> CreateClientAsync(
        [Description("ID del proyecto contenedor (formato 'prj_...').")] string projectId,
        [Description("Slug único dentro del proyecto (lowercase, a-z 0-9 -).")] string slug,
        [Description("Nombre display del client/tenant.")] string displayName,
        [Description("Descripción opcional.")] string? description,
        [Description("Email de contacto opcional.")] string? contactEmail,
        [Description("Tag de facturación opcional.")] string? billingTag,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        var result = await mediator
            .Send(new CreateClientCommand(projectId, slug, displayName, description, contactEmail, billingTag), ct)
            .ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_create_instance", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Crea una Instance = Template × Client × Environment en una VM target. Usa los puertos/volúmenes/"
        + "healthcheck por defecto del template (ajustables luego vía reconfigure/REST). Devuelve el detalle. "
        + "OJO: crear la instance NO la despliega — usá aethra_deploy_instance_native o aethra_deploy_app_environment después.")]
    public async Task<object> CreateInstanceAsync(
        [Description("ID del template (formato 'tpl_...').")] string templateId,
        [Description("ID del client/tenant (formato 'cli_...').")] string clientId,
        [Description("Environment configurado en Settings (ej. 'production', 'test').")] string environment,
        [Description("ID de la VM target donde correrá (formato 'vm_...').")] string targetVmId,
        [Description("Slug override opcional; null = se deriva de template+client.")] string? slugOverride,
        [Description("Si true, redeploya automáticamente al llegar un build nuevo de la rama trackeada.")] bool autoDeployOnNewBuild,
        [Description("Ref git a trackear (ej. 'refs/heads/main'). Null = cascada template/environment.")] string? trackedRef,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        var cmd = new CreateInstanceCommand(
            TemplateId: templateId,
            ClientId: clientId,
            Environment: environment,
            TargetVmId: targetVmId,
            SlugOverride: slugOverride,
            Ports: null,
            Volumes: null,
            Healthcheck: null,
            AutoDeployOnNewBuild: autoDeployOnNewBuild,
            TrackedRef: trackedRef);
        var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_create_template", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Crea un Template (definición de app desde un repo Git) dentro de un proyecto. El webhook secret se "
        + "genera en el server y por seguridad NO se devuelve por MCP — obtenelo en la UI o rotalo con "
        + "aethra_rotate_webhook_secret. BuildArgs y credencial de token se configuran vía UI/REST si se necesitan.")]
    public async Task<object> CreateTemplateAsync(
        [Description("ID del proyecto contenedor (formato 'prj_...').")] string projectId,
        [Description("Slug único dentro del proyecto (lowercase, a-z 0-9 -).")] string slug,
        [Description("Nombre display.")] string name,
        [Description("URL del repo Git (https).")] string gitRepoUrl,
        [Description("Branch por defecto (ej. 'main').")] string branch,
        [Description("Estrategia de build: 'Dockerfile', 'Compose' o 'Nixpacks'.")] string buildType,
        [Description("Descripción opcional.")] string? description,
        [Description("Subdirectorio base del repo (monorepo). Opcional.")] string? baseDirectory,
        [Description("Path al Dockerfile relativo al repo (para buildType=Dockerfile).")] string? dockerfilePath,
        [Description("Path al compose file (para buildType=Compose).")] string? composeFilePath,
        [Description("Nombre de la credencial (GitHubPat) para clonar repos privados. Opcional.")] string? accessTokenCredentialName,
        [Description("Globs que disparan rebuild al cambiar (ej. ['src/**']). Opcional.")] string[]? watchPaths,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        var cmd = new CreateTemplateCommand(
            ProjectId: projectId,
            Slug: slug,
            Name: name,
            Description: description,
            GitRepoUrl: gitRepoUrl,
            Branch: branch,
            BaseDirectory: baseDirectory,
            WatchPaths: watchPaths,
            AccessTokenCredentialName: accessTokenCredentialName,
            BuildType: buildType,
            DockerfilePath: dockerfilePath,
            ComposeFilePath: composeFilePath,
            BuildArgs: null,
            WebhookSecret: null);
        var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return McpResponses.FromError(result.Error);
        }
        var t = result.Value;
        return McpResponses.Ok(new
        {
            id = t.id,
            project_id = t.projectId,
            slug = t.slug,
            name = t.name,
            created_at = t.createdAt,
            webhook_secret_set = !string.IsNullOrEmpty(t.webhookSecret),
            note = "El webhook secret no se devuelve por MCP. Obtenelo en la UI o rotalo con aethra_rotate_webhook_secret.",
        });
    }

    [McpServerTool(Name = "aethra_list_instances", ReadOnly = true, OpenWorld = false)]
    [Description("Lista instances (Template × Client × Environment) con filtros opcionales. Read-only; "
        + "devuelve resúmenes sin env vars/secretos.")]
    public async Task<object> ListInstancesAsync(
        [Description("Filtrar por project_id (formato 'prj_...'). Omitir = todos.")] string? projectId,
        [Description("Filtrar por template_id (formato 'tpl_...'). Omitir = todos.")] string? templateId,
        [Description("Filtrar por client_id (formato 'cli_...'). Omitir = todos.")] string? clientId,
        [Description("Filtrar por efímeras/preview (true) o permanentes (false). Omitir = todas.")] bool? isEphemeral,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsRead);
        }
        var query = new ListInstancesQuery(
            TemplateId: templateId,
            ProjectId: projectId,
            OwnerUserId: null,
            IsEphemeral: isEphemeral,
            ClientId: clientId);
        var result = await mediator.Send(query, ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_get_instance", ReadOnly = true, OpenWorld = false)]
    [Description("Detalle de una instance: template/client/environment, slug, target VM, container, ports, volumes, "
        + "healthcheck, autodeploy y timestamps. Read-only; NO incluye env vars ni secretos (esos van por "
        + "aethra_list_env_vars / aethra_list_secrets).")]
    public async Task<object> GetInstanceAsync(
        [Description("ID de la instance (formato 'ins_...').")] string instanceId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsRead);
        }
        var result = await mediator.Send(new GetInstanceByIdQuery(instanceId), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_discover_repo", ReadOnly = true, OpenWorld = true)]
    [Description("(F9 stub) Analiza un repo Git para proponer Templates en F9.5.")]
    public object DiscoverRepo(
        [Description("URL del repositorio Git.")] string repoUrl,
        [Description("Branch a inspeccionar. Default: 'main'.")] string? branch)
    {
        _ = repoUrl;
        _ = branch;
        if (!caller.HasScope(McpScopes.ProjectsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsRead);
        }
        return McpResponses.NotImplemented("aethra_discover_repo", "F9.5");
    }

    [McpServerTool(Name = "aethra_create_application_from_git", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("(F9 stub) Se reescribirá en F9.5 como create_template_from_git + create_instance.")]
    public object CreateApplicationFromGit(
        [Description("URL del repositorio Git.")] string repoUrl,
        [Description("ID del proyecto destino.")] string? projectId,
        [Description("ID de la VM target.")] string? targetVmId,
        [Description("Hostname público sugerido.")] string? suggestedHostname,
        [Description("Vars iniciales como dict key→value.")] Dictionary<string, string>? envVars,
        [Description("Si true, no crea — sólo simula y devuelve un plan.")] bool dryRun = false)
    {
        _ = repoUrl;
        _ = projectId;
        _ = targetVmId;
        _ = suggestedHostname;
        _ = envVars;
        _ = dryRun;
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        return McpResponses.NotImplemented("aethra_create_application_from_git", "F9.5");
    }
}
