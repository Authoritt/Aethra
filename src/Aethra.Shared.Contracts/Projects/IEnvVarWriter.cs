namespace Aethra.Shared.Contracts.Projects;

/// <summary>
/// Permite a módulos externos (Services, Mcp) inyectar env vars no-secretas a cualquier scope
/// del modelo (Project, Template, Client, Instance) sin referenciar internals de Modules.Projects.
///
/// Para credenciales/secretos use <see cref="ISecretWriter"/> — las env vars planas viven en
/// una tabla; los secrets viven en otra cifrada con DataProtection. F9.1 cableará ambas tablas
/// y resoluciones.
/// </summary>
public interface IEnvVarWriter
{
    /// <summary>
    /// Upsert idempotente de un batch de env vars en el <paramref name="scope"/> indicado.
    /// Si una key ya existe con el mismo <paramref name="source"/>, se sobrescribe. Keys de
    /// otras sources no se tocan (un usuario manual no pierde su override).
    /// </summary>
    /// <param name="source">
    /// Origen lógico para auditoría y revoke selectivo. Ej: <c>"binding:bnd_..."</c>.
    /// </param>
    Task UpsertManyAsync(
        EnvVarScope scope,
        string scopeId,
        string source,
        IReadOnlyList<EnvVarUpsert> vars,
        CancellationToken ct);

    /// <summary>
    /// Borra todas las env vars previamente inyectadas por una <paramref name="source"/> dada
    /// dentro del <paramref name="scope"/>. Útil al revocar un ServiceBinding.
    /// </summary>
    Task RemoveBySourceAsync(
        EnvVarScope scope,
        string scopeId,
        string source,
        CancellationToken ct);
}

/// <summary>
/// Scopes válidos para variables de entorno y secretos. Cada nivel resuelve en cascada:
/// Instance &gt; Client &gt; Template &gt; Project (lo más específico gana).
/// </summary>
public enum EnvVarScope
{
    Project = 0,
    Template = 1,
    Client = 2,
    Instance = 3,
}

/// <summary>
/// Definición de una env var (no secreta) a inyectar. Para valores sensibles use
/// <see cref="SecretUpsert"/> a través de <see cref="ISecretWriter"/>.
/// </summary>
public sealed record EnvVarUpsert(
    string Key,
    string Value,
    bool IsBuildTime,
    bool IsRuntime);
