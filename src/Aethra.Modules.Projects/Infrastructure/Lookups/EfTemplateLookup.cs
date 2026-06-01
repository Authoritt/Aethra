using Aethra.Modules.Projects.Domain.Templates;
using Aethra.Shared.Contracts.Projects;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.Infrastructure.Lookups;

/// <summary>
/// Implementación EF Core de <see cref="ITemplateLookup"/>. Consulta los Templates persistidos
/// en <see cref="ProjectsDbContext"/> y los proyecta al <see cref="TemplateForBuildView"/> que
/// usan los módulos consumidores (Deployments, Webhooks).
///
/// Todas las queries usan <c>AsNoTracking</c> porque son lecturas cross-module — los Templates
/// solo se mutan dentro de los handlers de <c>Modules.Projects</c>.
/// </summary>
internal sealed class EfTemplateLookup(ProjectsDbContext db) : ITemplateLookup
{
    public async Task<IReadOnlyList<TemplateForBuildView>> FindByRepoAsync(
        string repoUrl, string branch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(repoUrl);
        ArgumentNullException.ThrowIfNull(branch);

        // Match por (Source.GitRepoUrl, Source.Branch). Como ambos son columnas owned de la
        // misma tabla, EF traduce esto a un WHERE simple sin joins. El converter de
        // <c>GitRepoUrl</c> permite comparar la columna como string sin instanciar el VO.
        var matches = await db.Templates
            .AsNoTracking()
            .Where(t => t.Source.GitRepoUrl.Value == repoUrl && t.Source.Branch == branch)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var result = new List<TemplateForBuildView>(matches.Count);
        foreach (var t in matches)
        {
            result.Add(Project(t));
        }
        return result;
    }

    public async Task<TemplateForBuildView?> GetByIdAsync(string templateId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(templateId);
        // El Id se persiste como string (ValueConverter). Comparamos su <c>ToString()</c>
        // para no acoplar el contrato cross-module al value-object TemplateId.
        var t = await db.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id.ToString() == templateId, ct)
            .ConfigureAwait(false);
        return t is null ? null : Project(t);
    }

    private static TemplateForBuildView Project(Template t)
        => new(
            TemplateId: t.Id.ToString(),
            ProjectId: t.ProjectId.ToString(),
            Slug: t.Slug.Value,
            Name: t.Name,
            GitRepoUrl: t.Source.GitRepoUrl.Value,
            Branch: t.Source.Branch,
            WebhookSecret: t.WebhookSecret,
            BaseDirectory: t.Source.BaseDirectory,
            WatchPaths: t.Source.WatchPaths,
            BuildType: t.Build.BuildType.ToString(),
            DockerfilePath: t.Build.DockerfilePath);
}
