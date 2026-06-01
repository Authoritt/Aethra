namespace Aethra.Shared.Contracts.Projects;

/// <summary>
/// Permite a módulos externos persistir secretos (credenciales, tokens) en el módulo Projects,
/// con cifrado DataProtection aplicado internamente. Los valores nunca se devuelven en claro al
/// caller — solo el orquestador de deploy los descifra justo antes de pasarlos al satélite.
///
/// F9.1 reescribirá la implementación para que use una tabla separada de la de env vars
/// planas — diseño explícito para reducir el blast-radius de un leak de la tabla principal.
///
/// <para>
/// <b>Semántica de persistencia:</b> las implementaciones llaman <c>SaveChangesAsync</c>
/// internamente sobre su propio <c>DbContext</c> (Projects). Invocar este writer ES un
/// punto-de-no-retorno: una vez retorna, los cambios están en BD. No hay rollback
/// cross-DbContext si el caller falla después.
/// </para>
/// </summary>
public interface ISecretWriter
{
    /// <summary>
    /// Upsert idempotente de un batch de secretos en el <paramref name="scope"/> indicado.
    /// Los <see cref="SecretUpsert.PlainValue"/> se cifran antes de persistirse.
    /// Persiste los cambios antes de retornar.
    /// </summary>
    Task UpsertManyAsync(
        EnvVarScope scope,
        string scopeId,
        string source,
        IReadOnlyList<SecretUpsert> secrets,
        CancellationToken ct);

    /// <summary>
    /// Borra todos los secretos previamente inyectados por una <paramref name="source"/> dada
    /// dentro del <paramref name="scope"/>. Útil al revocar un ServiceBinding.
    /// Persiste los cambios antes de retornar.
    /// </summary>
    Task RemoveBySourceAsync(
        EnvVarScope scope,
        string scopeId,
        string source,
        CancellationToken ct);
}

/// <summary>
/// Secreto a persistir. <see cref="PlainValue"/> se transmite en claro pero se cifra
/// inmediatamente en la implementación; nunca queda persistido en log ni telemetría.
/// </summary>
public sealed record SecretUpsert(string Key, string PlainValue);
