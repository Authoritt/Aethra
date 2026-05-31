using Aethra.Shared.Contracts.Projects;

namespace Aethra.Modules.Projects.Infrastructure.Lookups;

/// <summary>
/// Implementación no-op de <see cref="ISecretWriter"/>. No persiste nada.
/// F9.0 persistence sub-fase reemplazará esto con EF impl real que cifra los valores con
/// DataProtection antes de almacenarlos en la tabla <c>secrets</c>.
/// </summary>
internal sealed class NoOpSecretWriter : ISecretWriter
{
    public Task UpsertManyAsync(EnvVarScope scope, string scopeId, string source,
        IReadOnlyList<SecretUpsert> secrets, CancellationToken ct)
        => Task.CompletedTask;

    public Task RemoveBySourceAsync(EnvVarScope scope, string scopeId, string source, CancellationToken ct)
        => Task.CompletedTask;
}
