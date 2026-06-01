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
    [Description("Crea un servicio gestionado (Postgres, Redis, etc.) a partir de una plantilla. Genera credenciales admin cifradas internamente.")]
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

    [McpServerTool(Name = "aethra_bind_service", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Crea un binding (Instance ↔ ManagedService): provisiona credenciales y las inyecta como env vars (DATABASE_URL, etc).")]
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

    [McpServerTool(Name = "aethra_rotate_credentials", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Rota las credenciales de un ServiceBinding y reinyecta las env vars (la app necesitará redeploy/restart para tomarlas).")]
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
}
