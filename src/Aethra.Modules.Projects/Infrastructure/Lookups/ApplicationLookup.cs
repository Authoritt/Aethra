using Aethra.Shared.Contracts.Projects;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.Infrastructure.Lookups;

/// <summary>
/// Implementación EF Core de <see cref="IApplicationLookup"/>. Vive en Modules.Projects pero
/// expone la interface (en Shared.Contracts) para que módulos externos puedan consultar sin
/// romper la regla de aislamiento (NetArchTest valida que un módulo no referencie internals de otro).
/// </summary>
internal sealed class ApplicationLookup(ProjectsDbContext db) : IApplicationLookup
{
    public async Task<IReadOnlyList<ApplicationForDeployView>> FindByRepoAsync(
        string repoUrl, string branch, CancellationToken ct)
    {
        var normalizedBranch = branch.Trim();
        // Cargamos todas y filtramos en memoria — el caso ideal es pocas Apps por repo.
        // El value-converter de GitRepoUrl hace que la comparación SQL contra string sea complicada,
        // así que filtramos client-side. F5+: optimizar si hay miles de apps.
        var all = await db.Applications.AsNoTracking().ToListAsync(ct);
        return [.. all
            .Where(a => string.Equals(a.Source.GitRepoUrl.Value, repoUrl, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(a.Source.Branch, normalizedBranch, StringComparison.Ordinal))
            .Select(Project)];
    }

    public async Task<ApplicationForDeployView?> GetByIdAsync(string applicationId, CancellationToken ct)
    {
        var app = await db.Applications.AsNoTracking().FirstOrDefaultAsync(
            a => a.Id.ToString() == applicationId, ct);
        return app is null ? null : Project(app);
    }

    private static ApplicationForDeployView Project(Domain.Application a)
    {
        var primaryPort = a.Runtime.Ports.Count > 0 ? a.Runtime.Ports[0].ContainerPort.Value : (int?)null;
        return new ApplicationForDeployView(
            ApplicationId: a.Id.ToString(),
            EnvironmentId: a.EnvironmentId.ToString(),
            ProjectId: string.Empty,                       // proyecto se infiere via environment; F5+ exponerlo
            Slug: a.Slug.Value,
            Name: a.Name,
            GitRepoUrl: a.Source.GitRepoUrl.Value,
            Branch: a.Source.Branch,
            WebhookSecret: a.Source.WebhookSecret,
            BaseDirectory: a.Source.BaseDirectory,
            WatchPaths: a.Source.WatchPaths,
            TargetVmId: a.Runtime.TargetVmId,
            ContainerName: a.Runtime.ContainerName.Value,
            PrimaryContainerPort: primaryPort,
            BuildType: a.Build.Type.ToString(),
            BuildPath: a.Build.Path);
    }
}
