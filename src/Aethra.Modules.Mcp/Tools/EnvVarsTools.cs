using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Projects.UseCases.EnvVars.Commands;
using Aethra.Modules.Projects.UseCases.EnvVars.Queries;
using Aethra.Modules.Projects.UseCases.Secrets.Commands;
using Aethra.Modules.Projects.UseCases.Secrets.Queries;
using Aethra.Shared.Contracts.Projects;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

[McpServerToolType]
public sealed class EnvVarsTools(IEnvVarWriter envVarWriter, IMediator mediator, IMcpCallerContext caller)
{
    public sealed record EnvVarInput(
        string Key,
        string Value,
        bool IsBuildTime,
        bool IsRuntime);

    [McpServerTool(Name = "aethra_set_env_vars", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Upsert idempotente de env vars en un scope (project|template|client|instance). Cada var se etiqueta con el source 'mcp:apikey:{id}' para revoke selectivo."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
    public async Task<object> SetEnvVarsAsync(
        [Description("Tipo de scope: 'project', 'template', 'client' o 'instance'.")] string scopeType,
        [Description("ID del scope (prj_*, tpl_*, cli_*, ins_*).")] string scopeId,
        [Description("Lista de variables a inyectar.")] IReadOnlyList<EnvVarInput> vars,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        if (!Enum.TryParse<EnvVarScope>(scopeType, ignoreCase: true, out var scope) || !Enum.IsDefined(scope))
        {
            return McpResponses.Failure("env_vars.invalid_scope",
                $"scope_type='{scopeType}' inválido. Use project, template, client o instance.",
                "validation");
        }
        if (vars is null || vars.Count == 0)
        {
            return McpResponses.Failure("env_vars.empty", "vars no puede estar vacío.", "validation");
        }

        var upserts = vars.Select(v => new EnvVarUpsert(v.Key, v.Value, v.IsBuildTime, v.IsRuntime)).ToList();

        try
        {
            await envVarWriter.UpsertManyAsync(scope, scopeId, caller.AuditSource, upserts, ct).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            return McpResponses.Failure("env_vars.invalid", ex.Message, "validation");
        }
        return McpResponses.Ok(new
        {
            scope = scope.ToString(),
            scope_id = scopeId,
            count = upserts.Count,
            source = caller.AuditSource,
        });
    }

    [McpServerTool(Name = "aethra_list_env_vars", ReadOnly = true, OpenWorld = false)]
    [Description("Lista las env vars NO secretas de un scope (project|template|client|instance) con sus valores en "
        + "claro — las env vars planas no son secretas por diseño; los secretos viven aparte y NO se exponen por MCP. "
        + "Devuelve key, value, flags build_time/runtime/literal/multiline, source y timestamps.")]
    public async Task<object> ListEnvVarsAsync(
        [Description("Tipo de scope: 'project', 'template', 'client' o 'instance'.")] string scopeType,
        [Description("ID del scope (prj_*, tpl_*, cli_*, ins_*).")] string scopeId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsRead);
        }
        var result = await mediator.Send(new ListEnvVarsQuery(scopeType, scopeId), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_list_secrets", ReadOnly = true, OpenWorld = false)]
    [Description("Lista los SECRETOS de un scope (project|template|client|instance) SIN sus valores: sólo key, "
        + "has_value (indica que existe un cipher persistido), source y timestamps. El valor cifrado NUNCA se expone "
        + "por diseño. Útil para inventariar qué secretos están configurados sin filtrarlos.")]
    public async Task<object> ListSecretsAsync(
        [Description("Tipo de scope: 'project', 'template', 'client' o 'instance'.")] string scopeType,
        [Description("ID del scope (prj_*, tpl_*, cli_*, ins_*).")] string scopeId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsRead);
        }
        var result = await mediator.Send(new ListSecretsQuery(scopeType, scopeId), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_delete_env_var", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Elimina una env var (no secreta) por key de un scope (project|template|client|instance). "
        + "Usá dry_run=true primero para confirmar.")]
    public async Task<object> DeleteEnvVarAsync(
        [Description("Tipo de scope: 'project', 'template', 'client' o 'instance'.")] string scopeType,
        [Description("ID del scope (prj_*, tpl_*, cli_*, ins_*).")] string scopeId,
        [Description("Key de la env var a borrar.")] string key,
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
                wouldCall: $"delete env var '{key}' from {scopeType}:{scopeId}",
                plan: new { scopeType, scopeId, key, action = "delete non-secret env var" });
        }
        var result = await mediator.Send(new DeleteEnvVarCommand(scopeType, scopeId, key), ct).ConfigureAwait(false);
        return result.IsSuccess
            ? McpResponses.Ok(new { scope_type = scopeType, scope_id = scopeId, key, deleted = true })
            : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_delete_secret", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Elimina un secreto por key de un scope (borra el cipher persistido). El valor nunca se expuso. "
        + "Usá dry_run=true primero para confirmar.")]
    public async Task<object> DeleteSecretAsync(
        [Description("Tipo de scope: 'project', 'template', 'client' o 'instance'.")] string scopeType,
        [Description("ID del scope (prj_*, tpl_*, cli_*, ins_*).")] string scopeId,
        [Description("Key del secreto a borrar.")] string key,
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
                wouldCall: $"delete secret '{key}' from {scopeType}:{scopeId}",
                plan: new { scopeType, scopeId, key, action = "delete secret (removes persisted cipher)" });
        }
        var result = await mediator.Send(new DeleteSecretCommand(scopeType, scopeId, key), ct).ConfigureAwait(false);
        return result.IsSuccess
            ? McpResponses.Ok(new { scope_type = scopeType, scope_id = scopeId, key, deleted = true })
            : McpResponses.FromError(result.Error);
    }
}
