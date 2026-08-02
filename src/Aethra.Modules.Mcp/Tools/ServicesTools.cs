using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Services.UseCases.Commands;
using Aethra.Modules.Services.UseCases.Queries;
using Aethra.Shared.Contracts.Services;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

[McpServerToolType]
public sealed class ServicesTools(IMediator mediator, IMcpCallerContext caller)
{
    [McpServerTool(Name = "aethra_list_services", ReadOnly = true, OpenWorld = false)]
    [Description("Lista los servicios gestionados (Postgres/Redis/Rabbit) con sus counts de bindings activos.")]
    public async Task<object> ListAsync(CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ServicesRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ServicesRead);
        }
        var result = await mediator.Send(new ListServicesQuery(), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_get_service", ReadOnly = true, OpenWorld = false)]
    [Description("Devuelve el detalle de un Managed Service: imagen, puerto interno, red, estado, si está expuesto, "
        + "timestamps, conteo de bindings y, si falló el aprovisionamiento, error_code/error_message. "
        + "Read-only; NO devuelve credenciales ni connection strings.")]
    public async Task<object> GetServiceAsync(
        [Description("ID del Managed Service (formato 'svc_...').")] string serviceId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ServicesRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ServicesRead);
        }
        var result = await mediator.Send(new GetServiceByIdQuery(serviceId), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_list_service_templates", ReadOnly = true, OpenWorld = false)]
    [Description("Lista las plantillas disponibles (postgres, redis, etc.) que pueden usarse en aethra_create_service.")]
    public async Task<object> ListTemplatesAsync(CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ServicesRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ServicesRead);
        }
        var result = await mediator.Send(new ListTemplatesQuery(), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_create_service", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Crea un servicio gestionado (Postgres, Redis, etc.) a partir de una plantilla. Genera credenciales admin cifradas internamente."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
    public async Task<object> CreateServiceAsync(
        [Description("ID de la plantilla (ej. 'postgres-16', 'redis-7'). Lista vía aethra_list_service_templates.")] string templateId,
        [Description("Slug único (lowercase, a-z 0-9 -), max 64.")] string slug,
        [Description("Nombre display human-readable.")] string name,
        [Description("ID de la VM que alojará el contenedor.")] string targetVmId,
        [Description("Si true, expone el servicio a Internet (no aplica a todos los tipos).")] bool exposedExternally,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ServicesWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ServicesWrite);
        }
        var cmd = new CreateServiceFromTemplateCommand(templateId, slug, name, targetVmId, exposedExternally);
        var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_adopt_service", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Registra ('adopta') un contenedor que YA existe (creado fuera de Aethra) como ManagedService, "
        + "para que aparezca en /services y /data-services SIN provisionarlo ni recrearlo. Ideal para Postgres/"
        + "Redis levantados a mano. No toca el contenedor: solo guarda metadata + credenciales admin cifradas."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
    public async Task<object> AdoptServiceAsync(
        [Description("Slug único del servicio (ej. 'main-postgres', 'cache-redis').")] string slug,
        [Description("Nombre display human-readable.")] string name,
        [Description("Tipo: Postgres | Redis | RabbitMQ | MySQL | MongoDB | MariaDB | ClickHouse.")] string type,
        [Description("ID de la VM donde corre el contenedor existente.")] string targetVmId,
        [Description("Nombre real del contenedor Docker ya existente (ej. 'aethra-postgres').")] string containerName,
        [Description("Versión (ej. '16', '7'). Vacío = 'external'.")] string? version,
        [Description("Imagen del contenedor (ej. 'postgres:16-alpine'). Vacío = '(external)'.")] string? image,
        [Description("Puerto interno (Postgres 5432, Redis 6379...). 0 = default del tipo.")] int internalPort,
        [Description("Red Docker. Vacío = 'aethra-net'.")] string? networkName,
        [Description("Usuario admin del servicio existente. Vacío = 'admin'.")] string? adminUser,
        [Description("Password admin (para Redis sin auth, dejar vacío).")] string? adminPassword,
        [Description("Si true, marca el servicio como expuesto externamente.")] bool exposedExternally,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ServicesWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ServicesWrite);
        }
        var cmd = new AdoptServiceCommand(
            Slug: slug,
            Name: name,
            Type: type,
            Version: version ?? string.Empty,
            TargetVmId: targetVmId,
            ContainerName: containerName,
            Image: image ?? string.Empty,
            InternalPort: internalPort,
            NetworkName: networkName ?? "aethra-net",
            AdminUser: adminUser ?? string.Empty,
            AdminPassword: adminPassword ?? string.Empty,
            ExposedExternally: exposedExternally);
        var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_bind_service", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Crea un binding (Instance ↔ ManagedService): provisiona credenciales y las inyecta como env vars (DATABASE_URL, etc)."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
    public async Task<object> BindServiceAsync(
        [Description("ID de la Instance (formato 'ins_...').")] string instanceId,
        [Description("ID del ManagedService.")] string serviceId,
        [Description("Nombre del recurso interno (DB name, namespace, etc.). Si null, se infiere del slug de la instance.")] string? resourceName,
        [Description("Permisos: 'Owner', 'ReadWrite', 'ReadOnly'. Default 'ReadWrite'.")] string? permissions,
        [Description("Prefijo opcional para los nombres de env vars (ej. 'DB_').")] string? envVarPrefix,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ServicesWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ServicesWrite);
        }

        if (!Enum.TryParse<BindingPermissions>(permissions ?? "ReadWrite", ignoreCase: true, out var perm))
        {
            return McpResponses.Failure("binding.invalid_permissions",
                $"permissions='{permissions}' inválido. Use Owner, ReadWrite o ReadOnly.", "validation");
        }

        // MigrationsHook es un record con varios campos; la versión actual de la tool no lo expone
        // — se puede setear vía REST o en una iteración futura de la tool MCP (no bloquea binding).
        var cmd = new CreateBindingCommand(
            ServiceId: serviceId,
            InstanceId: instanceId,
            ResourceName: resourceName,
            Permissions: perm,
            EnvVarPrefix: envVarPrefix,
            MigrationsHook: null);
        var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_list_bindings", ReadOnly = true, OpenWorld = false)]
    [Description("Lista los bindings (Instance ↔ ManagedService) de un servicio: resource_name, permisos, "
        + "env_var_prefix y timestamps (creado/aprovisionado/revocado/última rotación). Por defecto sólo activos; "
        + "include_revoked=true incluye los ya revocados. Read-only; NO devuelve credenciales.")]
    public async Task<object> ListBindingsAsync(
        [Description("ID del Managed Service (formato 'svc_...').")] string serviceId,
        [Description("Si true, incluye bindings ya revocados. Default false (sólo activos).")] bool includeRevoked,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ServicesRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ServicesRead);
        }
        var result = await mediator.Send(new ListBindingsQuery(serviceId, includeRevoked), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_unbind_service", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Revoca un binding (Instance ↔ ManagedService): lo marca como revocado y des-aprovisiona las "
        + "credenciales inyectadas. La app afectada PERDERÁ acceso al servicio (requiere redeploy/restart). "
        + "Usá aethra_list_bindings para obtener el binding_id."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
    public async Task<object> UnbindServiceAsync(
        [Description("ID del ServiceBinding a revocar (formato 'bnd_...').")] string bindingId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ServicesWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ServicesWrite);
        }
        var result = await mediator.Send(new RevokeBindingCommand(bindingId), ct).ConfigureAwait(false);
        return result.IsSuccess
            ? McpResponses.Ok(new { binding_id = bindingId, revoked = true })
            : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_rotate_credentials", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Rota las credenciales de un ServiceBinding y reinyecta las env vars (la app necesitará redeploy/restart para tomarlas)."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
    public async Task<object> RotateCredentialsAsync(
        [Description("ID del ServiceBinding (formato 'bnd_...').")] string bindingId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ServicesWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ServicesWrite);
        }
        var result = await mediator.Send(new RotateCredentialsCommand(bindingId), ct).ConfigureAwait(false);
        return result.IsSuccess
            ? McpResponses.Ok(new { binding_id = bindingId, rotated = true })
            : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_delete_service", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Da de baja un Managed Service: lo marca como Stopped y lo deregistra de Aethra. "
        + "IMPORTANTE: NO toca el contenedor ni los datos reales (no borra el container, volumen ni la BD) y NO hace "
        + "hard-delete del registro — sólo lo saca de la gestión activa. Bloquea si el servicio tiene bindings "
        + "activos (revocalos antes con aethra_unbind_service). Usá dry_run=true primero para confirmar.")]
    public async Task<object> DeleteServiceAsync(
        [Description("ID del Managed Service (formato 'svc_...').")] string serviceId,
        [Description("Si true, NO ejecuta — devuelve el plan.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ServicesWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ServicesWrite);
        }
        if (dryRun)
        {
            return McpResponses.DryRun(
                wouldCall: $"DELETE /api/services/{serviceId}",
                plan: new
                {
                    serviceId,
                    action = "mark service Stopped + deregister (does NOT touch container/volume/DB)",
                    note = "Fails if there are active bindings; revoke them first.",
                });
        }
        var result = await mediator.Send(new DeleteServiceCommand(serviceId), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return McpResponses.FromError(result.Error);
        }

        // Antes esto devolvia status = "Stopped" como literal. Dos problemas, no uno:
        //  1. Afirmaba el estado del registro sin leerlo. Si el comando hubiera dejado otro, o una
        //     escritura concurrente lo hubiera cambiado, la respuesta habria dicho "Stopped" igual.
        //  2. El NOMBRE del campo invita a una inferencia mas fuerte que el hecho: esta baja NO
        //     para el contenedor (lo dice la descripcion de la tool), pero un agente que lee
        //     status="Stopped" concluira que el servicio dejo de correr. Ahora se dice explicito.
        string? estadoGuardado = null;
        string? motivoSinConfirmar = null;
        try
        {
            var releido = await mediator.Send(new GetServiceByIdQuery(serviceId), ct).ConfigureAwait(false);
            if (releido.IsSuccess)
            {
                estadoGuardado = releido.Value.Status;
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
#pragma warning disable CA1031 // Un fallo de la relectura es "no confirmable", no "no ejecutado".
        catch (Exception ex)
#pragma warning restore CA1031
        {
            motivoSinConfirmar = ex.GetType().Name;
        }

        return McpResponses.Ok(new
        {
            service_id = serviceId,
            deregistered = true,
            record_status = estadoGuardado,
            state_confirmed = estadoGuardado is not null,
            readback_error = motivoSinConfirmar,
            container_untouched = true,
            note = "record_status es el estado del REGISTRO en Aethra, no del contenedor: esta baja "
                + "no detiene el contenedor ni borra volumenes ni datos.",
        });
    }
}
