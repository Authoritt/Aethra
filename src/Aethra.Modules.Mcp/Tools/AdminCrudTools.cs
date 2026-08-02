using System.ComponentModel;
using Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands;
using Aethra.Modules.Identity.UseCases.Commands;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Projects.UseCases.Clients.Commands;
using Aethra.Modules.Projects.UseCases.Instances.Commands;
using Aethra.Modules.Projects.UseCases.Instances.Queries;
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
    /// <summary>
    /// Relee la Instance despues de escribirla y proyecta lo que quedo GUARDADO.
    ///
    /// <para>
    /// Estos setters devolvian el argumento del llamante como si fuera el estado resultante: si la
    /// escritura se hubiera recortado, ignorado o perdido contra una escritura concurrente, la
    /// respuesta habria sido identica byte a byte. Un agente no confirmaba nada salvo que recuerda
    /// lo que acaba de pedir. Ver issue #27.
    /// </para>
    ///
    /// <para>
    /// Tres desenlaces, no dos: escrito-y-confirmado, escrito-pero-no-confirmable (la relectura
    /// fallo), y error de escritura. El de en medio es el que un booleano aplastaria, y es justo el
    /// que el llamante necesita distinguir.
    /// </para>
    /// </summary>
    private async Task<object> InstanciaTrasEscribir(
        string instanceId,
        Func<Aethra.Modules.Projects.UseCases.Instances.Dtos.InstanceDetail, object> proyectar,
        CancellationToken ct)
    {
        // La relectura puede LANZAR, no solo devolver un Result fallido: ningun behavior del
        // pipeline convierte excepciones en Result (LoggingBehavior las registra y relanza). Sin
        // este catch, un fallo transitorio de BD saldria como error opaco de invocacion DESPUES de
        // que la escritura ya commiteo, y el llamante podria reintentar una mutacion hecha. La
        // cancelacion si tiene que propagar: es una peticion del llamante, no un fallo.
        Aethra.Modules.Projects.UseCases.Instances.Dtos.InstanceDetail? guardada = null;
        string? motivoSinConfirmar = null;
        try
        {
            var leida = await mediator.Send(new GetInstanceByIdQuery(instanceId), ct).ConfigureAwait(false);
            if (leida.IsSuccess)
            {
                guardada = leida.Value;
            }
            else
            {
                motivoSinConfirmar = "read_failed";
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Cualquier fallo de la relectura es "no confirmable", no "no escrito".
        catch (Exception ex)
#pragma warning restore CA1031
        {
            motivoSinConfirmar = ex.GetType().Name;
        }

        if (guardada is null)
        {
            return McpResponses.Ok(new
            {
                instance_id = instanceId,
                written = true,
                state_confirmed = false,
                note = "La escritura fue aceptada, pero no se pudo releer la Instance para confirmar "
                    + "el estado resultante. No se devuelve el valor pedido como si fuera el guardado. "
                    + "NO reintentes la escritura por esto: ya commiteo.",
                readback_error = motivoSinConfirmar,
            });
        }
        return McpResponses.Ok(proyectar(guardada));
    }

    // ---------------------------------------------------------------- Templates
    [McpServerTool(Name = "aethra_update_template", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Edita un Template (reemplaza name/description/source/build con los valores provistos — el slug NO cambia). " +
        "buildType debe ser Dockerfile|DockerCompose|Nixpacks. Pasa los valores actuales para los campos que no quieras cambiar."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
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
    [Description("Rota el webhook secret de un Template. Devuelve el nuevo secreto en claro UNA vez — guárdalo, no se vuelve a mostrar."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
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
        "(mapping del environment o branch por defecto). Ej. 'refs/heads/feature-x' o 'develop'."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
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
        if (!r.IsSuccess)
        {
            return McpResponses.FromError(r.Error);
        }
        return await InstanciaTrasEscribir(instanceId,
            // effective_tracked_ref es lo que de verdad se desplegara (se resuelve contra el
            // Template): un dato que el eco del argumento no podia dar nunca.
            i => new { instance_id = i.id, tracked_ref = i.trackedRef, effective_tracked_ref = i.effectiveTrackedRef, state_confirmed = true },
            ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "aethra_set_instance_autodeploy", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Habilita o deshabilita el auto-deploy de una Instance cuando hay un nuevo build verde del template."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
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
        if (!r.IsSuccess)
        {
            return McpResponses.FromError(r.Error);
        }
        return await InstanciaTrasEscribir(instanceId,
            i => new { instance_id = i.id, auto_deploy_on_new_build = i.autoDeployOnNewBuild, state_confirmed = true },
            ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "aethra_set_custom_domain", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Fija (o limpia) el dominio custom de una Instance. customDomain null/vacío vuelve al auto-hostname."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
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
        if (!r.IsSuccess)
        {
            return McpResponses.FromError(r.Error);
        }
        return await InstanciaTrasEscribir(instanceId,
            i => new { instance_id = i.id, custom_domain = i.customDomain, auto_hostname = i.autoHostname, state_confirmed = true },
            ct).ConfigureAwait(false);
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
    [Description("Actualiza info administrativa de un Client (display name/descripción/email/billing tag). El slug NO cambia."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
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
    [Description("Actualiza nombre y apariencia (descripción/color/icono) de un Project. El slug NO cambia."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
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
        "Slug/imagen/puerto/VM son inmutables. Devuelve el detalle actualizado."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
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
    [Description("Actualiza metadata editable de una VM (nombre, IPs pública/privada, descripción). El slug es inmutable."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
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
        "Reemplaza la lista de scopes completa — pasa los scopes actuales que quieras conservar."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
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
