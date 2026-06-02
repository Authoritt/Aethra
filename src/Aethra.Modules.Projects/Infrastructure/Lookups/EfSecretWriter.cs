using Aethra.Modules.Projects.Domain.EnvVars;
using Aethra.Modules.Projects.Domain.Secrets;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.Infrastructure.Lookups;

/// <summary>
/// Implementación EF Core de <see cref="ISecretWriter"/> (reemplaza al stub
/// <c>NoOpSecretWriter</c> de F9.0). Persiste secretos cifrados con DataProtection
/// (<see cref="ISecretCodec"/>) en la tabla separada <c>projects.secrets</c>.
///
/// Idempotencia: misma semántica que <see cref="EfEnvVarWriter"/> — sobrescribe si existe
/// (mismo <c>ScopeType + ScopeId + Key</c> dentro de la <c>Source</c>), no toca filas de otra
/// <c>Source</c>. Persiste con <c>SaveChangesAsync</c> antes de retornar (punto-de-no-retorno;
/// no hay transacción cross-DbContext).
/// </summary>
internal sealed class EfSecretWriter(ProjectsDbContext db, ISecretCodec codec, IClock clock) : ISecretWriter
{
    public async Task UpsertManyAsync(
        EnvVarScope scope,
        string scopeId,
        string source,
        IReadOnlyList<SecretUpsert> secrets,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(secrets);
        if (secrets.Count == 0)
        {
            return;
        }

        var scopeType = MapScope(scope);
        var now = clock.UtcNow;

        var keys = secrets.Select(s => s.Key.Trim()).ToList();
        var existing = await db.Secrets
            .Where(e => e.ScopeType == scopeType
                && e.ScopeId == scopeId
                && e.Source == source
                && keys.Contains(e.Key))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var existingByKey = existing.ToDictionary(e => e.Key, StringComparer.Ordinal);

        foreach (var s in secrets)
        {
            var key = s.Key.Trim();
            var cipher = codec.Encode(s.PlainValue);
            if (existingByKey.TryGetValue(key, out var current))
            {
                current.UpdateCipher(cipher, now);
            }
            else
            {
                db.Secrets.Add(Secret.Create(scopeType, scopeId, key, cipher, now, source));
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveBySourceAsync(
        EnvVarScope scope,
        string scopeId,
        string source,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        ArgumentNullException.ThrowIfNull(source);
        var scopeType = MapScope(scope);
        var toDelete = await db.Secrets
            .Where(e => e.ScopeType == scopeType && e.ScopeId == scopeId && e.Source == source)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (toDelete.Count == 0)
        {
            return;
        }
        db.Secrets.RemoveRange(toDelete);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static EnvScopeType MapScope(EnvVarScope scope) => scope switch
    {
        EnvVarScope.Project => EnvScopeType.Project,
        EnvVarScope.Template => EnvScopeType.Template,
        EnvVarScope.Client => EnvScopeType.Client,
        EnvVarScope.Instance => EnvScopeType.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Scope desconocido."),
    };
}
