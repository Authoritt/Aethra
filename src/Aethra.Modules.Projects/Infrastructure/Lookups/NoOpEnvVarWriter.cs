using Aethra.Shared.Contracts.Projects;

namespace Aethra.Modules.Projects.Infrastructure.Lookups;

/// <summary>
/// Implementación no-op de <see cref="IEnvVarWriter"/>. No persiste nada — completa la tarea
/// sin side-effects para no romper a los módulos consumidores (Services, Mcp) durante el
/// cleanup F9.0. F9.0 persistence sub-fase reemplazará esto con EF impl real.
/// </summary>
internal sealed class NoOpEnvVarWriter : IEnvVarWriter
{
    public Task UpsertManyAsync(EnvVarScope scope, string scopeId, string source,
        IReadOnlyList<EnvVarUpsert> vars, CancellationToken ct)
        => Task.CompletedTask;

    public Task RemoveBySourceAsync(EnvVarScope scope, string scopeId, string source, CancellationToken ct)
        => Task.CompletedTask;
}
