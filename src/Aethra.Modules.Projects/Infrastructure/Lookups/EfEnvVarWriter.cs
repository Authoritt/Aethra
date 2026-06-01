using Aethra.Modules.Projects.Domain.EnvVars;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.Infrastructure.Lookups;

/// <summary>
/// Implementación EF Core de <see cref="IEnvVarWriter"/>. Persiste env vars no-secretas en la
/// tabla polimórfica <c>env_vars</c> (un solo set, distinguido por <c>ScopeType</c> +
/// <c>ScopeId</c>).
///
/// <para>
/// Idempotencia: <see cref="UpsertManyAsync"/> sobrescribe si ya existe (mismo
/// <c>ScopeType + ScopeId + Key + Source</c>), pero NO toca filas con otra <c>Source</c> — un
/// usuario manual no pierde su override cuando un ServiceBinding actualiza sus vars.
/// </para>
///
/// <para>
/// <b>Semántica de persistencia:</b> cada método público llama
/// <c>SaveChangesAsync</c> al final sobre el <c>ProjectsDbContext</c>. Esto significa que
/// invocar el writer ES un punto-de-no-retorno: una vez retorna, los cambios están en BD.
/// Los handlers que llaman al writer deben asegurarse de que el resto de su trabajo (en su
/// propio DbContext) ya esté listo para persistir, dado que NO existe transacción
/// cross-DbContext y por tanto no hay rollback automático si su SaveChanges posterior falla.
/// </para>
/// </summary>
internal sealed class EfEnvVarWriter(ProjectsDbContext db, IClock clock) : IEnvVarWriter
{
    public async Task UpsertManyAsync(
        EnvVarScope scope,
        string scopeId,
        string source,
        IReadOnlyList<EnvVarUpsert> vars,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(vars);
        if (vars.Count == 0)
        {
            return;
        }

        var scopeType = MapScope(scope);
        var now = clock.UtcNow;

        // Pre-carga las vars existentes de este scope/source para decidir update vs insert sin
        // disparar N round-trips.
        var keys = vars.Select(v => v.Key.Trim()).ToList();
        var existing = await db.EnvironmentVariables
            .Where(e => e.ScopeType == scopeType
                && e.ScopeId == scopeId
                && e.Source == source
                && keys.Contains(e.Key))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var existingByKey = existing.ToDictionary(e => e.Key, StringComparer.Ordinal);

        foreach (var v in vars)
        {
            var key = v.Key.Trim();
            if (existingByKey.TryGetValue(key, out var current))
            {
                current.UpdateValue(v.Value ?? string.Empty, now);
                current.UpdateFlags(v.IsBuildTime, v.IsRuntime, isLiteral: null, isMultiline: null, now);
            }
            else
            {
                var fresh = EnvironmentVariable.Create(
                    scopeType: scopeType,
                    scopeId: scopeId,
                    key: key,
                    value: v.Value ?? string.Empty,
                    now: now,
                    isBuildTime: v.IsBuildTime,
                    isRuntime: v.IsRuntime,
                    source: source);
                db.EnvironmentVariables.Add(fresh);
            }
        }

        // Persistimos directamente: no existe TransactionBehavior global que cubra el
        // ProjectsDbContext del caller (Services/Mcp llaman desde su propio handler con su
        // propio DbContext). Si no se hace SaveChanges aquí, las env vars de un binding nunca
        // alcanzarían la BD.
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
        var toDelete = await db.EnvironmentVariables
            .Where(e => e.ScopeType == scopeType && e.ScopeId == scopeId && e.Source == source)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (toDelete.Count == 0)
        {
            return;
        }
        db.EnvironmentVariables.RemoveRange(toDelete);

        // Persistimos directamente. Ver nota en UpsertManyAsync sobre la ausencia de
        // TransactionBehavior global.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Traduce el enum cross-module (<see cref="EnvVarScope"/>) al enum del dominio interno
    /// (<see cref="EnvScopeType"/>). Los valores numéricos coinciden por contrato — si en algún
    /// momento se rompiera el alineamiento, este mapping centraliza la conversión.
    /// </summary>
    private static EnvScopeType MapScope(EnvVarScope scope) => scope switch
    {
        EnvVarScope.Project => EnvScopeType.Project,
        EnvVarScope.Template => EnvScopeType.Template,
        EnvVarScope.Client => EnvScopeType.Client,
        EnvVarScope.Instance => EnvScopeType.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Scope desconocido."),
    };
}
