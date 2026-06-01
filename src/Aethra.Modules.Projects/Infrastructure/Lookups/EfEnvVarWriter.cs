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
/// NO se ejecuta <c>SaveChangesAsync</c> dentro del writer: el caller (usualmente un handler
/// con <c>TransactionBehavior</c> activo) consolida los cambios en una sola transacción para
/// que el outbox y el cambio de estado se commiteen atómicamente.
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
