using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Projects.UseCases.EnvVars.Queries;
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
    [Description("Upsert idempotente de env vars en un scope (project|template|client|instance). Cada var se etiqueta con el source 'mcp:apikey:{id}' para revoke selectivo.")]
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
}
