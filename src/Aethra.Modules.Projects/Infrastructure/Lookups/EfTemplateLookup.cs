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
internal sealed class EfTemplateLookup(ProjectsDbContext db, IWebhookSecretCodec webhookCodec) : ITemplateLookup
{
    public async Task<IReadOnlyList<TemplateForBuildView>> FindByRepoAsync(
        string repoUrl, string branch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(repoUrl);
        ArgumentNullException.ThrowIfNull(branch);

        // El ValueConverter de GitRepoUrl impide a EF traducir t.Source.GitRepoUrl.Value == repoUrl.
        // Materializamos y filtramos en cliente — la cardinalidad de Templates es muy baja (decenas).
        // F12.3 — además del DefaultBranch, aceptamos push si la branch aparece en cualquier
        // EnvironmentMapping del Template (para soportar prod→main + staging→develop sin que el
        // push a develop sea descartado por no coincidir con DefaultBranch).
        var all = await db.Templates
            .AsNoTracking()
            .Include(t => t.EnvironmentMapping)
            .ToListAsync(ct).ConfigureAwait(false);
        var matches = all.Where(t =>
                string.Equals(t.Source.GitRepoUrl.Value, repoUrl, StringComparison.Ordinal)
                && (string.Equals(t.Source.DefaultBranch, branch, StringComparison.Ordinal)
                    || t.EnvironmentMapping.Any(m => string.Equals(m.Branch, branch, StringComparison.Ordinal))))
            .ToList();

        return matches.Select(Project).ToList();
    }

    public async Task<IReadOnlyList<TemplateForBuildView>> FindAllByRepoAsync(
        string repoUrl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(repoUrl);
        // Mismo motivo: materializar antes de filtrar por el converted property.
        var all = await db.Templates.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var matches = all.Where(t =>
                string.Equals(t.Source.GitRepoUrl.Value, repoUrl, StringComparison.Ordinal))
            .ToList();
        return matches.Select(Project).ToList();
    }

    public async Task<TemplateForBuildView?> GetByIdAsync(string templateId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(templateId);
        // El Id se persiste como string (ValueConverter). Materializamos AsEnumerable porque
        // EF no traduce `Id.ToString() == arg` con el ValueConverter activo. Cardinalidad baja
        // por GetByIdAsync (un único lookup), aceptable.
        var all = await db.Templates.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var t = all.FirstOrDefault(x => x.Id.ToString() == templateId);
        return t is null ? null : Project(t);
    }

    private TemplateForBuildView Project(Template t)
        => new(
            TemplateId: t.Id.ToString(),
            ProjectId: t.ProjectId.ToString(),
            Slug: t.Slug.Value,
            Name: t.Name,
            GitRepoUrl: t.Source.GitRepoUrl.Value,
            Branch: t.Source.DefaultBranch,
            // El cipher se descifra en el read-model para que los consumidores (webhook
            // validator) puedan validar HMAC. Sólo vive en memoria del proceso API; nunca
            // se serializa fuera del proceso.
            WebhookSecret: webhookCodec.Decode(t.WebhookSecretCipher),
            BaseDirectory: t.Source.BaseDirectory,
            WatchPaths: t.Source.WatchPaths,
            BuildType: t.Build.BuildType.ToString(),
            DockerfilePath: t.Build.DockerfilePath,
            ComposeFilePath: t.Build.ComposeFilePath,
            AutoPreviewPullRequests: t.AutoPreviewPullRequests,
            Services: t.Services
                .Select(s => new TemplateServiceView(s.Name, s.Image, s.Port, s.PathPrefixes, s.Env, s.BuildMode, s.DockerfilePath))
                .ToList());
}
