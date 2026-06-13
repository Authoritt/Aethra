using Aethra.Modules.Projects.Domain.Templates;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.UseCases.Templates.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Templates.Queries;

/// <summary>
/// Detalle de un <c>Template</c>. Expone los flat fields de <c>Source</c> y <c>Build</c>; el
/// <c>WebhookSecret</c> NO se expone aquí (solo en la respuesta del create).
/// </summary>
public sealed record GetTemplateByIdQuery(string TemplateId) : IQuery<TemplateDetail>;

internal sealed class GetTemplateByIdHandler(ProjectsDbContext db)
    : IQueryHandler<GetTemplateByIdQuery, TemplateDetail>
{
    public async Task<Result<TemplateDetail>> Handle(
        GetTemplateByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.TemplateId, out var parsed) || parsed.Value.Prefix != "tpl")
        {
            return Error.Validation("template.invalid_id", "ID de template inválido.");
        }
        var templateId = new TemplateId(parsed.Value);

        var t = await db.Templates
            .AsNoTracking()
            .Include(x => x.EnvironmentMapping)
            .FirstOrDefaultAsync(x => x.Id == templateId, cancellationToken)
            .ConfigureAwait(false);

        if (t is null)
        {
            return Error.NotFound("template.not_found", $"Template '{request.TemplateId}' no existe.");
        }

        IReadOnlyList<TemplateBuildArgDto> args =
            [.. t.Build.BuildArgs.Select(a => new TemplateBuildArgDto(a.Key, a.Value))];
        IReadOnlyList<TemplateEnvironmentMappingDto> mappings =
            [.. t.EnvironmentMapping.Select(m => new TemplateEnvironmentMappingDto(m.Environment, m.Branch))];
        IReadOnlyList<TemplateServiceDto> services =
            [.. t.Services.Select(s => new TemplateServiceDto(
                s.Name, s.Image, s.Port, s.PathPrefixes,
                [.. s.Env.Select(e => new TemplateBuildArgDto(e.Key, e.Value))],
                s.BuildMode, s.DockerfilePath,
                [.. (s.Volumes ?? []).Select(v => new TemplateServiceVolumeDto(v.Name, v.ContainerPath, v.ReadOnly))],
                s.Hostname))];

        return new TemplateDetail(
            id: t.Id.ToString(),
            projectId: t.ProjectId.ToString(),
            slug: t.Slug.Value,
            name: t.Name,
            description: t.Description,
            gitRepoUrl: t.Source.GitRepoUrl.Value,
            branch: t.Source.DefaultBranch,
            baseDirectory: t.Source.BaseDirectory,
            watchPaths: t.Source.WatchPaths,
            accessTokenCredentialName: t.Source.AccessTokenCredentialName,
            buildType: t.Build.BuildType.ToString(),
            dockerfilePath: t.Build.DockerfilePath,
            composeFilePath: t.Build.ComposeFilePath,
            buildArgs: args,
            createdAt: t.CreatedAt,
            updatedAt: t.UpdatedAt,
            environmentMapping: mappings,
            autoPreviewPullRequests: t.AutoPreviewPullRequests,
            services: services);
    }
}
