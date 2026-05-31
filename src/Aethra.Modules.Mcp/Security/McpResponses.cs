using Aethra.Shared.Kernel.Errors;

namespace Aethra.Modules.Mcp.Security;

/// <summary>
/// Helpers para que las tools devuelvan errores estructurados JSON al cliente MCP
/// (cualquier excepción que escape de una tool quedaría como un error opaco del SDK).
/// </summary>
internal static class McpResponses
{
    /// <summary>Forma estable: <c>{ ok: false, error: { code, message, type } }</c>.</summary>
    public static object Failure(string code, string message, string? type = null) => new
    {
        ok = false,
        error = new
        {
            code,
            message,
            type = type ?? "failure",
        },
    };

    public static object FromError(Error error) => Failure(error.Code, error.Message, error.Type.ToString().ToLowerInvariant());

    /// <summary>Falta de scope. El cliente debería pedir una API key con el scope indicado.</summary>
    public static object InsufficientScope(string required) => Failure(
        code: "insufficient_scope",
        message: $"Se requiere el scope '{required}' para invocar esta tool.",
        type: "forbidden");

    /// <summary>Tool reconocida pero no implementada todavía (devuelve la fase que la entregará).</summary>
    public static object NotImplemented(string toolName, string plannedIn) => Failure(
        code: "not_implemented",
        message: $"La tool '{toolName}' aún no está implementada. Planeada para {plannedIn}.",
        type: "failure");

    /// <summary>Éxito uniforme: <c>{ ok: true, data }</c>.</summary>
    public static object Ok(object data) => new { ok = true, data };
}
