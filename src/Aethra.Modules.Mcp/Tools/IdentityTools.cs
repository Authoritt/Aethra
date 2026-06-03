using System.ComponentModel;
using Aethra.Modules.Identity.UseCases.Commands;
using Aethra.Modules.Identity.UseCases.Queries;
using Aethra.Modules.Mcp.Security;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

/// <summary>
/// F11.5 — herramientas para gestionar Users + Roles via MCP. Encajan con los endpoints REST
/// <c>/api/identity/users</c> y <c>/api/identity/roles</c> y reutilizan los mismos comandos
/// MediatR (validación, idempotencia, side-effects: todos comunes).
///
/// <para>
/// Nota: el plan listaba <c>aethra_assign_role</c> y <c>aethra_revoke_user</c>; el módulo Identity
/// expone esas operaciones como <see cref="UpdateUserCommand"/> (replace de roles) y
/// <see cref="DeactivateUserCommand"/> (soft-delete) respectivamente. Las tools MCP envuelven
/// esas semánticas con la convención simple del catálogo (<c>assign_role</c> agrega un rol al set
/// actual, <c>revoke_user</c> desactiva el user).
/// </para>
/// </summary>
[McpServerToolType]
public sealed class IdentityTools(IMediator mediator, IMcpCallerContext caller)
{
    [McpServerTool(Name = "aethra_create_user", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Crea un user con un set inicial de roles. El password se hashea con Argon2 + DataProtection. " +
        "Devuelve {id,email,roles}. Ejemplo: email='ops@empresa.com', password='change-me-1234', role_slugs=['operator'].")]
    public async Task<object> CreateUserAsync(
        [Description("Email único del user. Se normaliza a lowercase.")] string email,
        [Description("Password inicial (>=8 chars). El user puede cambiarlo después.")] string password,
        [Description("Array de slugs de roles existentes. Ejemplo: ['admin'] o ['operator','viewer'].")] IReadOnlyList<string> roleSlugs,
        [Description("Display name opcional (ej. 'Juan Pérez').")] string? displayName,
        [Description("Si true, NO crea — devuelve plan + endpoint REST que se hubiera llamado.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.UsersWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.UsersWrite);
        }
        if (dryRun)
        {
            return McpResponses.DryRun(
                wouldCall: "POST /api/identity/users",
                plan: new { email, displayName, roleSlugs },
                nextActions: [new McpResponses.NextAction(
                    Tool: "aethra_create_user",
                    Why: "Re-ejecutá sin dry_run para crear el user.",
                    SuggestedArgs: new { email, password = "***", role_slugs = roleSlugs, display_name = displayName })]);
        }

        var result = await mediator.Send(
            new CreateUserCommand(email, password, displayName, roleSlugs), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return McpResponses.FromError(result.Error);
        }
        return McpResponses.OkWithNextActions(
            result.Value,
            nextActions:
            [
                new McpResponses.NextAction(
                    Tool: "aethra_list_users",
                    Why: "Verificá que el user aparece en el listado con sus roles correctos.",
                    SuggestedArgs: null),
            ]);
    }

    [McpServerTool(Name = "aethra_list_users", ReadOnly = true, OpenWorld = false)]
    [Description("Lista todos los users con sus roles, estado activo y timestamps. Read-only.")]
    public async Task<object> ListUsersAsync(CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.UsersRead))
        {
            return McpResponses.InsufficientScope(McpScopes.UsersRead);
        }
        var result = await mediator.Send(new ListUsersQuery(), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_assign_role", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Agrega un rol al user (sin remover los demás). Internamente lee el set actual de roles, " +
        "agrega el slug nuevo y hace replace via UpdateUserCommand. Idempotente: si el user ya lo tiene, no falla.")]
    public async Task<object> AssignRoleAsync(
        [Description("ID del user (formato 'usr_...').")] string userId,
        [Description("Slug del rol a agregar (ej. 'admin', 'operator', 'viewer' o un custom).")] string roleSlug,
        [Description("Si true, NO ejecuta — devuelve plan.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.UsersWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.UsersWrite);
        }

        // Necesitamos el set actual de roles para hacer un "add" (UpdateUserCommand reemplaza).
        var users = await mediator.Send(new ListUsersQuery(), ct).ConfigureAwait(false);
        if (!users.IsSuccess)
        {
            return McpResponses.FromError(users.Error);
        }
        var target = users.Value.FirstOrDefault(u => u.Id == userId);
        if (target is null)
        {
            return McpResponses.Failure("user.not_found", $"User '{userId}' no existe.", "not_found");
        }
        var newSlugs = target.Roles.Select(r => r.Slug).ToList();
        var normalizedNew = roleSlug.Trim().ToLowerInvariant();
        if (!newSlugs.Contains(normalizedNew, StringComparer.OrdinalIgnoreCase))
        {
            newSlugs.Add(normalizedNew);
        }

        if (dryRun)
        {
            return McpResponses.DryRun(
                wouldCall: $"PATCH /api/identity/users/{userId}",
                plan: new { userId, current_roles = target.Roles.Select(r => r.Slug), new_roles = newSlugs },
                nextActions: [new McpResponses.NextAction(
                    Tool: "aethra_assign_role",
                    Why: "Re-ejecutá sin dry_run para aplicar el cambio.",
                    SuggestedArgs: new { user_id = userId, role_slug = roleSlug })]);
        }

        var result = await mediator.Send(
            new UpdateUserCommand(userId, DisplayName: null, RoleSlugs: newSlugs), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return McpResponses.FromError(result.Error);
        }
        return McpResponses.OkWithNextActions(
            data: new { user_id = userId, role_slug = normalizedNew, total_roles = newSlugs.Count },
            nextActions:
            [
                new McpResponses.NextAction(
                    Tool: "aethra_list_users",
                    Why: "Confirmá el set de roles final del user.",
                    SuggestedArgs: null),
            ]);
    }

    [McpServerTool(Name = "aethra_revoke_user", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Soft-delete: marca el user como inactivo (IsActive=false). El login lo rechaza. " +
        "Las referencias históricas (notes, audit) se preservan. Falla si es el último admin activo.")]
    public async Task<object> RevokeUserAsync(
        [Description("ID del user (formato 'usr_...').")] string userId,
        [Description("Si true, NO ejecuta — devuelve plan.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.UsersWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.UsersWrite);
        }
        if (dryRun)
        {
            return McpResponses.DryRun(
                wouldCall: $"DELETE /api/identity/users/{userId}",
                plan: new { userId, action = "deactivate (soft-delete)" });
        }
        var result = await mediator.Send(new DeactivateUserCommand(userId), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return McpResponses.FromError(result.Error);
        }
        return McpResponses.OkWithNextActions(
            data: new { user_id = userId, deactivated = true },
            nextActions:
            [
                new McpResponses.NextAction(
                    Tool: "aethra_list_users",
                    Why: "Verificá que el user aparece como IsActive=false.",
                    SuggestedArgs: null),
            ]);
    }

    [McpServerTool(Name = "aethra_list_roles", ReadOnly = true, OpenWorld = false)]
    [Description("Lista todos los roles (sistema + custom) con sus scopes. Read-only.")]
    public async Task<object> ListRolesAsync(CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.UsersRead))
        {
            return McpResponses.InsufficientScope(McpScopes.UsersRead);
        }
        var result = await mediator.Send(new ListRolesQuery(), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_create_custom_role", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Crea un rol custom con un set arbitrario de scopes. Los slugs system ('admin','operator','viewer') " +
        "están reservados. Ejemplo: slug='deploy-bot', scopes=['deployments:trigger','context:read'].")]
    public async Task<object> CreateCustomRoleAsync(
        [Description("Slug único (lowercase, a-z 0-9 -), max 64.")] string slug,
        [Description("Nombre display (ej. 'Deploy Bot').")] string displayName,
        [Description("Lista de scopes válidos del catálogo (ver ApiKey.AllScopes).")] IReadOnlyList<string> scopes,
        [Description("Si true, NO crea — devuelve plan.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.UsersWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.UsersWrite);
        }
        if (dryRun)
        {
            return McpResponses.DryRun(
                wouldCall: "POST /api/identity/roles",
                plan: new { slug, displayName, scopes });
        }
        var result = await mediator.Send(
            new CreateRoleCommand(slug, displayName, scopes), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return McpResponses.FromError(result.Error);
        }
        return McpResponses.OkWithNextActions(
            data: result.Value,
            nextActions:
            [
                new McpResponses.NextAction(
                    Tool: "aethra_create_user",
                    Why: "Creá un user que asuma este rol (o usá aethra_assign_role sobre uno existente).",
                    SuggestedArgs: new { role_slugs = new[] { slug } }),
            ]);
    }
}
