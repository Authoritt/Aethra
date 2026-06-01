using Aethra.Shared.Contracts.Projects;

namespace Aethra.Modules.Projects.Infrastructure.Lookups;

/// <summary>
/// Implementación stub de <see cref="ISecretWriter"/> hasta que F9.1 introduzca la tabla
/// <c>secrets</c> cifrada con DataProtection. Por ahora completa la tarea sin persistir nada,
/// permitiendo que los handlers que dependen del contrato (CreateBinding, RotateCredentials)
/// sigan funcionando contra env vars planas mientras se diseña la nueva entidad.
/// </summary>
// TODO F9.1: implementar EfSecretWriter + Secret entity + tabla cifrada (DataProtection),
// con ValueConverter para el cipher text y un SecretLookup análogo a ITemplateLookup.
internal sealed class NoOpSecretWriter : ISecretWriter
{
    public Task UpsertManyAsync(EnvVarScope scope, string scopeId, string source,
        IReadOnlyList<SecretUpsert> secrets, CancellationToken ct)
        => Task.CompletedTask;

    public Task RemoveBySourceAsync(EnvVarScope scope, string scopeId, string source, CancellationToken ct)
        => Task.CompletedTask;
}
