using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using Aethra.Shared.Contracts.Projects;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

[McpServerToolType]
public sealed class EnvVarsTools(IEnvVarWriter envVarWriter, IMcpCallerContext caller)
{
    public sealed record EnvVarInput(
        string Key,
        string Value,
        bool IsBuildTime,
        bool IsRuntime,
        bool IsSecret);

    [McpServerTool(Name = "aethra_set_env_vars", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Upsert idempotente de env vars en una Application. Cada var se etiqueta con el source 'mcp:apikey:{id}' para revoke selectivo.")]
    public async Task<object> SetEnvVarsAsync(
        [Description("Tipo de scope. Actualmente sólo se admite 'application' — Environment/Project quedan para F7 (planeado).")] string scopeType,
        [Description("ID de la Application (formato 'app_...').")] string scopeId,
        [Description("Lista de variables a inyectar.")] IReadOnlyList<EnvVarInput> vars,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        if (!string.Equals(scopeType, "application", StringComparison.OrdinalIgnoreCase))
        {
            return McpResponses.Failure("env_vars.unsupported_scope",
                $"scope_type='{scopeType}' aún no soportado por la tool MCP. Solo 'application' por ahora.",
                "validation");
        }
        if (vars is null || vars.Count == 0)
        {
            return McpResponses.Failure("env_vars.empty", "vars no puede estar vacío.", "validation");
        }

        var upserts = vars.Select(v => new EnvVarUpsert(v.Key, v.Value, v.IsBuildTime, v.IsRuntime, v.IsSecret)).ToList();

        try
        {
            await envVarWriter.UpsertManyAsync(scopeId, caller.AuditSource, upserts, ct).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            return McpResponses.Failure("env_vars.invalid", ex.Message, "validation");
        }
        return McpResponses.Ok(new
        {
            application_id = scopeId,
            count = upserts.Count,
            source = caller.AuditSource,
        });
    }
}
