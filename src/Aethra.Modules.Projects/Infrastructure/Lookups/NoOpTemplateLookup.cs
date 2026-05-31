using Aethra.Shared.Contracts.Projects;

namespace Aethra.Modules.Projects.Infrastructure.Lookups;

/// <summary>
/// Implementación no-op de <see cref="ITemplateLookup"/>. Devuelve listas vacías / null.
/// F9.0 persistence sub-fase reemplazará esto con EF impl real apoyada en
/// <c>ProjectsDbContext.Templates</c>.
/// </summary>
internal sealed class NoOpTemplateLookup : ITemplateLookup
{
    public Task<IReadOnlyList<TemplateForBuildView>> FindByRepoAsync(string repoUrl, string branch, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<TemplateForBuildView>>([]);

    public Task<TemplateForBuildView?> GetByIdAsync(string templateId, CancellationToken ct)
        => Task.FromResult<TemplateForBuildView?>(null);
}
