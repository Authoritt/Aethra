using Aethra.Modules.Projects.Domain.Templates;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.UseCases.Templates.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Primitives;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Templates.Commands;

/// <summary>
/// Edita un Template existente (reemplaza name/description/source/build con los valores provistos —
/// el form de edición pre-carga los actuales). El slug NO cambia (rompería webhooks). Reutiliza los
/// métodos de dominio Rename/UpdateDescription/UpdateSource/UpdateBuild.
/// </summary>
public sealed record UpdateTemplateCommand(
    string TemplateId,
    string Name,
    string? Description,
    string GitRepoUrl,
    string Branch,
    string? BaseDirectory,
    IReadOnlyList<string>? WatchPaths,
    string? AccessTokenCredentialName,
    string BuildType,
    string? DockerfilePath,
    string? ComposeFilePath,
    IReadOnlyList<TemplateBuildArgDto>? BuildArgs) : ICommand;

public sealed class UpdateTemplateValidator : AbstractValidator<UpdateTemplateCommand>
{
    public UpdateTemplateValidator()
    {
        RuleFor(c => c.TemplateId).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(255);
        RuleFor(c => c.Description).MaximumLength(2000);
        RuleFor(c => c.GitRepoUrl).NotEmpty().MaximumLength(500);
        RuleFor(c => c.Branch).NotEmpty().MaximumLength(255);
        RuleFor(c => c.BuildType)
            .NotEmpty()
            .Must(bt => Enum.TryParse<TemplateBuildType>(bt, ignoreCase: true, out _))
            .WithMessage("BuildType debe ser uno de: Dockerfile, DockerCompose, Nixpacks.");
    }
}

internal sealed class UpdateTemplateHandler(ProjectsDbContext db, IClock clock) : ICommandHandler<UpdateTemplateCommand>
{
    public async Task<Result> Handle(UpdateTemplateCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.TemplateId, out var parsed) || parsed.Value.Prefix != "tpl")
        {
            return Error.Validation("template.invalid_id", "ID de template inválido.");
        }
        var templateId = new TemplateId(parsed.Value);

        var template = await db.Templates.FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken).ConfigureAwait(false);
        if (template is null)
        {
            return Error.NotFound("template.not_found", $"Template '{request.TemplateId}' no existe.");
        }

        var repoResult = GitRepoUrl.Create(request.GitRepoUrl);
        if (repoResult.IsFailure)
        {
            return repoResult.Error;
        }

        var now = clock.UtcNow;
        try
        {
            var source = TemplateSource.Create(
                repoResult.Value, request.Branch, request.BaseDirectory, request.WatchPaths, request.AccessTokenCredentialName);

            var buildType = Enum.Parse<TemplateBuildType>(request.BuildType, ignoreCase: true);
            var args = request.BuildArgs is { Count: > 0 }
                ? request.BuildArgs.Select(a => new KeyValuePair<string, string>(a.key, a.value)).ToList()
                : null;
            var build = buildType switch
            {
                TemplateBuildType.Dockerfile => TemplateBuild.Dockerfile(request.DockerfilePath, args),
                TemplateBuildType.DockerCompose => TemplateBuild.DockerCompose(request.ComposeFilePath, args),
                TemplateBuildType.Nixpacks => TemplateBuild.Nixpacks(args),
                _ => TemplateBuild.Dockerfile(request.DockerfilePath, args),
            };

            template.Rename(request.Name, now);
            template.UpdateDescription(request.Description, now);
            template.UpdateSource(source, now);
            template.UpdateBuild(build, now);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("template.invalid_source_or_build", ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
