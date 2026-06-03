using System.ComponentModel;
using Aethra.Modules.Identity.UseCases.Commands;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Projects.UseCases.Instances.Commands;
using Aethra.Modules.Projects.UseCases.Instances.Queries;
using Aethra.Modules.Projects.UseCases.Templates.Commands;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

/// <summary>
/// F12.3 — Tools para que agentes IA gestionen Branch-per-Instance + Preview deployments.
/// Reutilizan los Commands MediatR (validación, idempotencia, side-effects atómicos comunes
/// con el frontend REST). Scopes: <c>projects:read</c> / <c>projects:write</c> / <c>users:write</c>.
/// </summary>
[McpServerToolType]
public sealed class PreviewsTools(IMediator mediator, IMcpCallerContext caller)
{
    [McpServerTool(Name = "aethra_list_previews", ReadOnly = true, OpenWorld = false)]
    [Description("Lista las Instances ephemerals (PR previews). Filtros opcionales: project_id, template_id, only_mine (true reemplaza owner_id por el caller actual).")]
    public async Task<object> ListPreviewsAsync(
        [Description("Filtrar por proyecto (opcional).")] string? projectId,
        [Description("Filtrar por template (opcional).")] string? templateId,
        [Description("Filtrar por userId Aethra dueño de la Instance.")] string? ownerUserId,
        [Description("Si true, sólo previews del caller actual.")] bool onlyMine,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsRead);
        }
        var effectiveOwner = onlyMine ? caller.UserId : ownerUserId;
        var result = await mediator.Send(new ListInstancesQuery(
            TemplateId: templateId,
            ProjectId: projectId,
            OwnerUserId: effectiveOwner,
            IsEphemeral: true), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_delete_preview", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Borra una Instance ephemeral (preview). Idempotente: si ya no existe, devuelve ok. " +
        "Emite los integration events para que Proxy/Containers/Cloudflare limpien cascada.")]
    public async Task<object> DeletePreviewAsync(
        [Description("ID de la Instance (formato 'ins_...').")] string instanceId,
        [Description("Si true, NO ejecuta — devuelve plan.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        if (dryRun)
        {
            return McpResponses.DryRun(
                wouldCall: $"DELETE /api/instances/{instanceId}",
                plan: new { instanceId, action = "delete_ephemeral_instance" });
        }
        var result = await mediator.Send(new DeleteInstanceCommand(instanceId, ForceEphemeral: false), ct)
            .ConfigureAwait(false);
        return result.IsSuccess
            ? McpResponses.Ok(new { instance_id = instanceId, deleted = true })
            : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_set_environment_mapping", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Reemplaza el mapping Environment→Branch de un Template. Pasar lista vacía limpia el mapping. " +
        "Ejemplo: template_id='tpl_abc', mappings=[{environment:'production',branch:'main'},{environment:'staging',branch:'develop'}].")]
    public async Task<object> SetEnvironmentMappingAsync(
        [Description("ID del Template (formato 'tpl_...').")] string templateId,
        [Description("Mappings a setear como pares environment+branch.")] IReadOnlyList<EnvironmentMappingItemDto> mappings,
        [Description("Si true, NO ejecuta — devuelve plan.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        if (dryRun)
        {
            return McpResponses.DryRun(
                wouldCall: $"PATCH /api/templates/{templateId}/environment-mapping",
                plan: new { template_id = templateId, mappings });
        }
        var result = await mediator.Send(new SetEnvironmentMappingCommand(templateId, mappings ?? []), ct)
            .ConfigureAwait(false);
        return result.IsSuccess
            ? McpResponses.Ok(new { template_id = templateId, applied = mappings?.Count ?? 0 })
            : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_set_auto_preview", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Habilita o deshabilita el auto-create de Instances ephemerals al recibir un pull_request webhook.")]
    public async Task<object> SetAutoPreviewAsync(
        [Description("ID del Template.")] string templateId,
        [Description("true = habilitar; false = deshabilitar.")] bool enabled,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        var result = await mediator.Send(new SetAutoPreviewCommand(templateId, enabled), ct).ConfigureAwait(false);
        return result.IsSuccess
            ? McpResponses.Ok(new { template_id = templateId, auto_preview_pull_requests = enabled })
            : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_update_user_profile", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Actualiza el profile del propio usuario: por ahora solo gitHubUsername. " +
        "El handle se usa para mapear PR.user.login → User al crear previews.")]
    public async Task<object> UpdateUserProfileAsync(
        [Description("Handle de GitHub. Pasar null o string vacío + clear=true para limpiar.")] string? gitHubUsername,
        [Description("Si true, fuerza la limpieza del campo gitHubUsername.")] bool clearGitHubUsername,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.UsersWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.UsersWrite);
        }
        var userId = caller.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return McpResponses.Failure("user.not_a_real_user",
                "El caller no tiene un userId Aethra asociado (¿API key sin owner?).",
                "validation");
        }
        var result = await mediator.Send(new UpdateUserCommand(
            UserId: userId,
            DisplayName: null,
            RoleSlugs: null,
            GitHubUsername: gitHubUsername,
            ClearGitHubUsername: clearGitHubUsername), ct).ConfigureAwait(false);
        return result.IsSuccess
            ? McpResponses.Ok(new { user_id = userId, github_username = gitHubUsername, cleared = clearGitHubUsername })
            : McpResponses.FromError(result.Error);
    }
}
