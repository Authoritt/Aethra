using Aethra.Modules.Projects.Domain.EnvVars;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.Infrastructure.Lookups;

/// <summary>
/// Implementación EF Core de <see cref="IEnvVarWriter"/>. Expone al mundo (vía Shared.Contracts)
/// la capacidad de inyectar/revocar env vars en una Application sin romper la regla de aislamiento.
///
/// Estrategia para F5: las vars inyectadas viajan con <c>Source = "binding:{bindingId}"</c>.
/// Al revocar el binding, este writer borra todas las vars que tengan ese source — los overrides
/// manuales (source = null) sobreviven.
/// </summary>
internal sealed class EnvVarWriter(ProjectsDbContext db, IClock clock) : IEnvVarWriter
{
    public async Task UpsertManyAsync(string applicationId, string source,
        IReadOnlyList<EnvVarUpsert> vars, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            throw new ArgumentException("ApplicationId requerido.", nameof(applicationId));
        }
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Source requerido.", nameof(source));
        }
        if (vars.Count == 0) { return; }

        var existing = await db.EnvironmentVariables
            .Where(v => v.ScopeType == EnvScopeType.Application && v.ScopeId == applicationId)
            .ToListAsync(ct);
        var byKey = existing.ToDictionary(v => v.Key, StringComparer.Ordinal);

        var now = clock.UtcNow;
        foreach (var upsert in vars)
        {
            if (byKey.TryGetValue(upsert.Key, out var current))
            {
                // Solo actualizamos si la var pertenece a este source (o no tiene source). Si un usuario
                // tiene un override manual con misma key, no lo pisamos — gana el manual.
                if (current.Source is null || current.Source == source)
                {
                    current.UpdateValue(upsert.Value, now);
                    current.UpdateFlags(upsert.IsBuildTime, upsert.IsRuntime, upsert.IsSecret, null, null, now);
                }
                continue;
            }
            var ev = EnvironmentVariable.Create(
                EnvScopeType.Application,
                applicationId,
                upsert.Key,
                upsert.Value,
                now,
                isBuildTime: upsert.IsBuildTime,
                isRuntime: upsert.IsRuntime,
                isSecret: upsert.IsSecret,
                source: source);
            db.EnvironmentVariables.Add(ev);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveBySourceAsync(string applicationId, string source, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(applicationId) || string.IsNullOrWhiteSpace(source))
        {
            return;
        }
        await db.EnvironmentVariables
            .Where(v => v.ScopeType == EnvScopeType.Application
                     && v.ScopeId == applicationId
                     && v.Source == source)
            .ExecuteDeleteAsync(ct);
    }
}
