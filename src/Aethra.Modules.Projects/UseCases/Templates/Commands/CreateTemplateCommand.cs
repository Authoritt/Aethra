using System.Text.RegularExpressions;
using Aethra.Modules.Projects.Domain;
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
/// Crea un <c>Template</c> dentro de un <c>Project</c>. Acepta los <see cref="TemplateSource"/> y
/// <see cref="TemplateBuild"/> como flat fields para no acoplar la API al value-object shape —
/// el handler los compone. El <see cref="WebhookSecret"/> es opcional: si null, se genera un
/// secret hex 32-char en el handler (no en el aggregate, para devolverlo al caller).
/// </summary>
public sealed record CreateTemplateCommand(
    string ProjectId,
    string Slug,
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
    IReadOnlyList<TemplateBuildArgDto>? BuildArgs,
    string? WebhookSecret) : ICommand<TemplateCreatedResult>;

public sealed partial class CreateTemplateValidator : AbstractValidator<CreateTemplateCommand>
{
    public CreateTemplateValidator()
    {
        RuleFor(c => c.ProjectId).NotEmpty();
        RuleFor(c => c.Slug)
            .NotEmpty()
            .MaximumLength(31)
            .Matches(TemplateSlugRegex())
            .WithMessage(
                "Slug inválido. Debe empezar con letra minúscula, contener solo letras, dígitos o guion, y tener máximo 31 caracteres.");
        RuleFor(c => c.Name).NotEmpty().MaximumLength(255);
        RuleFor(c => c.Description).MaximumLength(2000);
        RuleFor(c => c.GitRepoUrl).NotEmpty().MaximumLength(500);
        RuleFor(c => c.Branch).NotEmpty().MaximumLength(255);
        RuleFor(c => c.BuildType)
            .NotEmpty()
            .Must(bt => Enum.TryParse<TemplateBuildType>(bt, ignoreCase: true, out _))
            .WithMessage("BuildType debe ser uno de: Dockerfile, DockerCompose, Nixpacks.");
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{0,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex TemplateSlugRegex();
}

internal sealed class CreateTemplateHandler(ProjectsDbContext db, IWebhookSecretCodec webhookCodec, IClock clock)
    : ICommandHandler<CreateTemplateCommand, TemplateCreatedResult>
{
    public async Task<Result<TemplateCreatedResult>> Handle(
        CreateTemplateCommand request,
        CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.ProjectId, out var parsedProject) || parsedProject.Value.Prefix != "prj")
        {
            return Error.Validation("template.invalid_project_id", "ID de proyecto inválido.");
        }
        var projectId = new ProjectId(parsedProject.Value);

        if (!await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("template.project_not_found", $"Proyecto '{request.ProjectId}' no existe.");
        }

        var slugResult = Slug.Create(request.Slug);
        if (slugResult.IsFailure)
        {
            return slugResult.Error;
        }
        var slug = slugResult.Value;

        if (await db.Templates
                .AnyAsync(t => t.ProjectId == projectId && t.Slug == slug, cancellationToken)
                .ConfigureAwait(false))
        {
            return Error.Conflict(
                "template.slug_taken",
                $"Ya existe un template con slug '{slug.Value}' en este proyecto.");
        }

        var repoResult = GitRepoUrl.Create(request.GitRepoUrl);
        if (repoResult.IsFailure)
        {
            return repoResult.Error;
        }

        TemplateSource source;
        TemplateBuild build;
        try
        {
            source = TemplateSource.Create(
                repoResult.Value,
                request.Branch,
                request.BaseDirectory,
                request.WatchPaths,
                request.AccessTokenCredentialName);

            var buildType = Enum.Parse<TemplateBuildType>(request.BuildType, ignoreCase: true);
            var args = request.BuildArgs is { Count: > 0 }
                ? request.BuildArgs.Select(a => new KeyValuePair<string, string>(a.key, a.value)).ToList()
                : null;

            build = buildType switch
            {
                TemplateBuildType.Dockerfile => TemplateBuild.Dockerfile(request.DockerfilePath, args),
                TemplateBuildType.DockerCompose => TemplateBuild.DockerCompose(request.ComposeFilePath, args),
                TemplateBuildType.Nixpacks => TemplateBuild.Nixpacks(args),
                _ => TemplateBuild.Dockerfile(request.DockerfilePath, args),
            };
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("template.invalid_source_or_build", ex.Message);
        }

        // El secret se calcula aquí (no en el aggregate) para poder devolverlo en plain al caller.
        // Si el cliente pasa uno explícito, lo respetamos (útil para rehidratar desde backup).
        // El aggregate lo cifra internamente con DataProtection.
        var webhookSecretPlain = string.IsNullOrWhiteSpace(request.WebhookSecret)
            ? Template.GenerateWebhookSecret()
            : request.WebhookSecret.Trim();

        Template template;
        try
        {
            template = Template.Create(
                projectId,
                slug,
                request.Name,
                source,
                build,
                webhookSecretPlain,
                webhookCodec,
                clock.UtcNow,
                request.Description);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("template.invalid", ex.Message);
        }

        db.Templates.Add(template);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new TemplateCreatedResult(
            id: template.Id.ToString(),
            projectId: template.ProjectId.ToString(),
            slug: template.Slug.Value,
            name: template.Name,
            webhookSecret: webhookSecretPlain,
            createdAt: template.CreatedAt);
    }
}
