namespace Aethra.Modules.Projects.Infrastructure.Lookups;

/// <summary>
/// Política de pisado entre orígenes (<c>Source</c>) para env vars y secretos. El índice único es
/// (scope, key) SIN source, así que una clave existente con otro origen no puede reinsertarse:
/// hay que decidir si se actualiza o se respeta.
/// </summary>
internal static class SourcePolicy
{
    /// <summary>Origen humano: UI/REST (<c>manual:</c>) o MCP (<c>mcp:</c>).</summary>
    public static bool IsHuman(string? source) =>
        source is not null
        && (source.StartsWith("manual:", StringComparison.Ordinal)
            || source.StartsWith("mcp:", StringComparison.Ordinal));

    /// <summary>
    /// Decide si <paramref name="incoming"/> puede escribir sobre una fila de <paramref name="existing"/>.
    /// Misma source: sí. Escritor humano: sí (es la intención del usuario). Escritor automático
    /// (bindings) sobre fila humana: NO (override preservado). Ambos automáticos: sí (último escritor).
    /// </summary>
    public static bool CanOverwrite(string? existing, string incoming) =>
        string.Equals(existing, incoming, StringComparison.Ordinal)
        || IsHuman(incoming)
        || !IsHuman(existing);
}
