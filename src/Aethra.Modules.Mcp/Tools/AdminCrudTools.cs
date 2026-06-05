using System.ComponentModel;
using Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands;
using Aethra.Modules.Identity.UseCases.Commands;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Projects.UseCases.Clients.Commands;
using Aethra.Modules.Projects.UseCases.Instances.Commands;
using Aethra.Modules.Projects.UseCases.Projects.Commands;
using Aethra.Modules.Projects.UseCases.Templates.Commands;
using Aethra.Modules.Projects.UseCases.Templates.Dtos;
using Aethra.Modules.Services.UseCases.Commands;
using Aethra.Modules.Vms.UseCases.Vms.Commands;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

/// <summary>
/// Tools de CRUD administrativo (update/delete) para Templates, Instances, Clients, Projects y
/// ManagedServices. Reutilizan los Commands MediatR (misma validación / side-effects que el REST).
/// Scope <c>projects:write</c> salvo update_service (<c>services:write</c>).
/// </summary>
[McpServerToolType]
public sealed class AdminCrudTools(IMediator mediator, IMcpCallerContext caller)
{
    // ---------------------------------------------------------------- Templates
    [McpServerTool(Name = "aethra_update_template", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Edita un Template (reemplaza name/description/source/build con los valores provistos — el slug NO cambia). " +
        "buildType debe ser Dockerfile|DockerCompose|Nixpacks. Pasa los valores actuales para los campos que no quieras cambiar.")]
    public async Task<object> UpdateTemplateAsync(
        [Description("ID del Template (formato 'tpl_...').")] string templateId,
        [Description("Nombre display.")] string name,
        [Description("Descripción (opcional).")] string? description,
        [Description("URL del repo Git.")] string gitRepoUrl,
        [Description("Branch por defecto (ej. 'main').")] string branch,
        [Description("Subdirectorio base del build (default '/').")] string? baseDirectory,
        [Description("BuildType: Dockerfile, DockerCompose o Nixpacks.")] string buildType,
        [Description("Path al Dockerfile (si buildType=Dockerfile).")] string? dockerfilePath,
        [Description("Path al compose (si buildType=DockerCompose).")] string? composeFilePath,
        [Description("Build args (opcional).")] IReadOnlyList<TemplateBuildArgDto>? buildArgs,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        var cmd = new UpdateTemplateCommand(
            templateId, name, description, gitRepoUrl, branch, baseDirectory,
            WatchPaths: null, AccessTokenCredentialName: null, buildType, dockerfilePath, composeFilePath, buildArgs);
        var r = await mediator.Send(cmd, ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(new { template_id = templateId, updated = true }) : McpResponses.FromError(r.Error);
    }

    [McpServerTool(Name = "aethra_delete_template", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Borra un Template. Si tiene instancias requiere force=true (cascada: instancias + env vars + secrets). " +
        "No detiene contenedores ni limpia rutas — eso lo hacen los integration events.")]
    public async Task<object> DeleteTemplateAsync(
        [Description("ID del Template.")] string templateId,
        [Description("true = cascada de instancias.")] bool force,
        [Description("Si true, NO ejecuta — devuelve plan.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        if (dryRun)
        {
            return McpResponses.DryRun($"DELETE /api/templates/{templateId}?force={force}", new { templateId, force });
        }
        var r = await mediator.Send(new DeleteTemplateCommand(templateId, force), ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(new { template_id = templateId, deleted = true }) : McpResponses.FromError(r.Error);
    }

    [McpServerTool(Name = "aethra_rotate_webhook_secret", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Rota el webhook secret de un Template. Devuelve el nuevo secreto en claro UNA vez — guárdalo, no se vuelve a mostrar.")]
    public async Task<object> RotateWebhookSecretAsync(
        [Description("ID del Template.")] string templateId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        var r = await mediator.Send(new RotateWebhookSecretCommand(templateId), ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(r.Value) : McpResponses.FromError(r.Error);
    }

    // ---------------------------------------------------------------- Instances
    [McpServerTool(Name = "aethra_set_instance_tracked_ref", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Fija (o limpia) la rama que despliega una Instance. trackedRef null/vacío = heredar del template " +
        "(mapping del environment o branch por defecto). Ej. 'refs/heads/feature-x' o 'develop'.")]
    public async Task<object> SetInstanceTrackedRefAsync(
        [Description("ID de la Instance (formato 'ins_...').")] string instanceId,
        [Description("Rama explícita, o null/vacío para heredar del template.")] string? trackedRef,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        var value = string.IsNullOrWhiteSpace(trackedRef) ? null : trackedRef.Trim();
        var r = await mediator.Send(new SetTrackedRefCommand(instanceId, value), ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(new { instance_id = instanceId, tracked_ref = value }) : McpResponses.FromError(r.Error);
    }

    [McpServerTool(Name = "aethra_set_instance_autodeploy", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Habilita o deshabilita el auto-deploy de una Instance cuando hay un nuevo build verde del template.")]
    public async Task<object> SetInstanceAutoDeployAsync(
        [Description("ID de la Instance.")] string instanceId,
        [Description("true = habilitar; false = deshabilitar.")] bool enabled,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        var r = await mediator.Send(new SetAutoDeployCommand(instanceId, enabled), ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(new { instance_id = instanceId, auto_deploy_on_new_build = enabled }) : McpResponses.FromError(r.Error);
    }

    [McpServerTool(Name = "aethra_set_custom_domain", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Fija (o limpia) el dominio custom de una Instance. customDomain null/vacío vuelve al auto-hostname.")]
    public async Task<object> SetCustomDomainAsync(
        [Description("ID de la Instance.")] string instanceId,
        [Description("Dominio custom (ej. 'app.cliente.com'), o null/vacío para limpiar.")] string? customDomain,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        var value = string.IsNullOrWhiteSpace(customDomain) ? null : customDomain.Trim();
        var r = await mediator.Send(new SetCustomDomainCommand(instanceId, value), ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(new { instance_id = instanceId, custom_domain = value }) : McpResponses.FromError(r.Error);
    }

    [McpServerTool(Name = "aethra_delete_instance", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Borra una Instance (también las no-ephemeral con force=true). Emite los integration events para que " +
        "Proxy/Containers/Cloudflare limpien en cascada. No detiene contenedores directamente.")]
    public async Task<object> DeleteInstanceAsync(
        [Description("ID de la Instance.")] string instanceId,
        [Description("true = permite borrar instancias no-ephemeral.")] bool force,
        [Description("Si true, NO ejecuta — devuelve plan.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        if (dryRun)
        {
            return McpResponses.DryRun($"DELETE /api/instances/{instanceId}?force={force}", new { instanceId, force });
        }
        var r = await mediator.Send(new DeleteInstanceCommand(instanceId, force), ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(new { instance_id = instanceId, deleted = true }) : McpResponses.FromError(r.Error);
    }

    // ---------------------------------------------------------------- Clients
    [McpServerTool(Name = "aethra_update_client", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Actualiza info administrativa de un Client (display name/descripción/email/billing tag). El slug NO cambia.")]
    public async Task<object> UpdateClientAsync(
        [Description("ID del Client (formato 'cli_...').")] string clientId,
        [Description("Nombre display.")] string displayName,
        [Description("Descripción (opcional).")] string? description,
        [Description("Email de contacto (opcional).")] string? contactEmail,
        [Description("Billing tag (opcional).")] string? billingTag,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        var r = await mediator.Send(new UpdateClientCommand(clientId, displayName, description, contactEmail, billingTag), ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(new { client_id = clientId, updated = true }) : McpResponses.FromError(r.Error);
    }

    [McpServerTool(Name = "aethra_delete_client", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Borra un Client. Si tiene instancias requiere force=true (cascada: instancias + env vars + secrets).")]
    public async Task<object> DeleteClientAsync(
        [Description("ID del Client.")] string clientId,
        [Description("true = cascada de instancias asociadas.")] bool force,
        [Description("Si true, NO ejecuta — devuelve plan.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        if (dryRun)
        {
            return McpResponses.DryRun($"DELETE /api/clients/{clientId}?force={force}", new { clientId, force });
        }
        var r = await mediator.Send(new DeleteClientCommand(clientId, force), ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(new { client_id = clientId, deleted = true }) : McpResponses.FromError(r.Error);
    }

    // ---------------------------------------------------------------- Projects
    [McpServerTool(Name = "aethra_update_project", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Actualiza nombre y apariencia (descripción/color/icono) de un Project. El slug NO cambia.")]
    public async Task<object> UpdateProjectAsync(
        [Description("ID del Project (formato 'prj_...').")] string projectId,
        [Description("Nombre display.")] string name,
        [Description("Descripción (opcional).")] string? description,
        [Description("Color hex (opcional, ej. '#10b981').")] string? color,
        [Description("Nombre del icono (opcional).")] string? icon,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        var r = await mediator.Send(new UpdateProjectCommand(projectId, name, description, color, icon), ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(new { project_id = projectId, updated = true }) : McpResponses.FromError(r.Error);
    }

    // ---------------------------------------------------------------- Services
    [McpServerTool(Name = "aethra_update_service", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Actualiza metadata editable de un ManagedService: nombre display y exposición externa. " +
        "Slug/imagen/puerto/VM son inmutables. Devuelve el detalle actualizado.")]
    public async Task<object> UpdateServiceAsync(
        [Description("ID del ManagedService (formato 'svc_...').")] string serviceId,
        [Description("Nombre display.")] string name,
        [Description("true = expone el servicio a Internet.")] bool exposedExternally,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ServicesWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ServicesWrite);
        }
        var r = await mediator.Send(new UpdateServiceCommand(serviceId, name, exposedExternally), ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(r.Value) : McpResponses.FromError(r.Error);
    }

    // ---------------------------------------------------------------- Cloudflare Tunnel
    [McpServerTool(Name = "aethra_delete_tunnel", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Borra el túnel Cloudflare gestionado del registro de Aethra. NO toca la config remota en " +
        "Cloudflare (el túnel sigue existiendo allá); solo desvincula a Aethra de su gestión.")]
    public async Task<object> DeleteTunnelAsync(
        [Description("Si true, NO ejecuta — devuelve plan.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.CloudflareWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.CloudflareWrite);
        }
        if (dryRun)
        {
            return McpResponses.DryRun("DELETE /api/cloudflare/tunnel", new { });
        }
        var r = await mediator.Send(new DeleteTunnelCommand(), ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(new { tunnel = "deleted" }) : McpResponses.FromError(r.Error);
    }

    // ---------------------------------------------------------------- Vms
    [McpServerTool(Name = "aethra_update_vm", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Actualiza metadata editable de una VM (nombre, IPs pública/privada, descripción). El slug es inmutable.")]
    public async Task<object> UpdateVmAsync(
        [Description("ID de la VM (formato 'vm_...').")] string vmId,
        [Description("Nombre display.")] string name,
        [Description("IP pública (opcional).")] string? publicIp,
        [Description("IP privada (opcional).")] string? privateIp,
        [Description("Descripción (opcional).")] string? description,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.VmsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.VmsWrite);
        }
        var r = await mediator.Send(new UpdateVmCommand(vmId, name, publicIp, privateIp, description), ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(new { vm_id = vmId, updated = true }) : McpResponses.FromError(r.Error);
    }

    [McpServerTool(Name = "aethra_delete_vm", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Borra una VM del registro de Aethra (junto con su satélite/token). force=true por simetría; " +
        "la limpieza de instancias/contenedores que apuntaban a la VM es responsabilidad del caller.")]
    public async Task<object> DeleteVmAsync(
        [Description("ID de la VM.")] string vmId,
        [Description("true = fuerza el borrado (simetría de API).")] bool force,
        [Description("Si true, NO ejecuta — devuelve plan.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.VmsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.VmsWrite);
        }
        if (dryRun)
        {
            return McpResponses.DryRun($"DELETE /api/vms/{vmId}?force={force}", new { vmId, force });
        }
        var r = await mediator.Send(new DeleteVmCommand(vmId, force), ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(new { vm_id = vmId, deleted = true }) : McpResponses.FromError(r.Error);
    }

    // ---------------------------------------------------------------- Identity Roles
    [McpServerTool(Name = "aethra_update_role", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Edita un rol custom (displayName + scopes). Los roles del sistema no se pueden modificar. " +
        "Reemplaza la lista de scopes completa — pasa los scopes actuales que quieras conservar.")]
    public async Task<object> UpdateRoleAsync(
        [Description("ID del Role (formato 'rol_...').")] string roleId,
        [Description("Nombre display.")] string displayName,
        [Description("Lista completa de scopes del rol.")] IReadOnlyList<string> scopes,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.UsersWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.UsersWrite);
        }
        var r = await mediator.Send(new UpdateRoleCommand(roleId, displayName, scopes), ct).ConfigureAwait(false);
        return r.IsSuccess ? McpResponses.Ok(new { role_id = roleId, updated = true }) : McpResponses.FromError(r.Error);
    }
}
