namespace Aethra.Shared.Contracts.Projects;

/// <summary>
/// Permite a módulos externos (Services) **inyectar** env vars a nivel Application
/// sin referenciar internals de Modules.Projects.
///
/// Caso de uso F5: al crear un ServiceBinding, el provisioner genera credenciales y
/// se las inyecta como env vars a la Application (DATABASE_URL, POSTGRES_*, REDIS_URL, etc.).
/// Las variables marcadas con <c>isSecret=true</c> se cifran con DataProtection en
/// Projects antes de persistirlas — el módulo externo no maneja el cifrado.
///
/// La implementación vive en Modules.Projects.Infrastructure y se registra en su <c>AddProjectsModule</c>.
/// </summary>
public interface IEnvVarWriter
{
    /// <summary>
    /// Upsert idempotente de un batch de env vars en una Application. Si una key ya existe
    /// con el mismo <paramref name="source"/>, se sobrescribe. Keys de otras sources no se tocan
    /// (un usuario manual no pierde su override).
    /// </summary>
    /// <param name="source">
    /// Origen lógico para auditoría y para revoke selectivo. Ej: <c>"binding:bnd_01H..."</c>.
    /// </param>
    Task UpsertManyAsync(
        string applicationId,
        string source,
        IReadOnlyList<EnvVarUpsert> vars,
        CancellationToken ct);

    /// <summary>
    /// Borra todas las env vars previamente inyectadas por una <paramref name="source"/> dada.
    /// Útil al revocar un ServiceBinding: limpia DATABASE_URL/etc. inyectadas por ese binding.
    /// </summary>
    Task RemoveBySourceAsync(
        string applicationId,
        string source,
        CancellationToken ct);
}

/// <summary>
/// Definición de una env var a inyectar. <paramref name="IsSecret"/> hace que Projects cifre
/// el valor antes de persistirlo (oculto en UI, expuesto sólo al deployar).
/// </summary>
public sealed record EnvVarUpsert(
    string Key,
    string Value,
    bool IsBuildTime,
    bool IsRuntime,
    bool IsSecret);
