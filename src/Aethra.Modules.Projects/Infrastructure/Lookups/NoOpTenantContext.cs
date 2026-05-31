using Aethra.Shared.Contracts.Projects;

namespace Aethra.Modules.Projects.Infrastructure.Lookups;

/// <summary>
/// Implementación no-op de <see cref="ITenantContext"/>. Devuelve <c>null</c> siempre.
/// F9.0 persistence sub-fase reemplazará esto con un wrapper sobre <see cref="IInstanceLookup"/>
/// que devuelva el ClientId real.
/// </summary>
internal sealed class NoOpTenantContext : ITenantContext
{
    public Task<string?> GetClientIdForInstanceAsync(string instanceId, CancellationToken ct)
        => Task.FromResult<string?>(null);
}
