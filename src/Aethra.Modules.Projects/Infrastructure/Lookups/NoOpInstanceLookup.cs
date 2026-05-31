using Aethra.Shared.Contracts.Projects;

namespace Aethra.Modules.Projects.Infrastructure.Lookups;

/// <summary>
/// Implementación no-op de <see cref="IInstanceLookup"/>. Devuelve listas vacías / null.
/// F9.0 persistence sub-fase reemplazará esto con EF impl real apoyada en
/// <c>ProjectsDbContext.Instances</c>.
/// </summary>
internal sealed class NoOpInstanceLookup : IInstanceLookup
{
    public Task<InstanceForDeployView?> GetByIdAsync(string instanceId, CancellationToken ct)
        => Task.FromResult<InstanceForDeployView?>(null);

    public Task<IReadOnlyList<InstanceForDeployView>> FindByTemplateAsync(
        string templateId, bool autoDeployOnly, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<InstanceForDeployView>>([]);

    public Task<IReadOnlyList<InstanceForDeployView>> FindByClientAsync(
        string clientId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<InstanceForDeployView>>([]);
}
