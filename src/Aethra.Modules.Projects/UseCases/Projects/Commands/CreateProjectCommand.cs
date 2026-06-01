using System.Text.RegularExpressions;
using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.UseCases.Projects.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Primitives;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Projects.Commands;

/// <summary>
/// Crea un <c>Project</c>. <see cref="Slug"/> debe ser único globalmente; el handler revalida en
/// BD por defensa más allá del validator (race entre dos POSTs concurrentes).
/// </summary>
public sealed record CreateProjectCommand(
    string Slug,
    string Name,
    string? Description,
    string? Color,
    string? Icon) : ICommand<ProjectDetail>;

public sealed partial class CreateProjectValidator : AbstractValidator<CreateProjectCommand>
{
    public CreateProjectValidator()
    {
        RuleFor(c => c.Slug)
            .NotEmpty()
            .MaximumLength(31)
            .Matches(ProjectSlugRegex())
            .WithMessage(
                "Slug inválido. Debe empezar con letra minúscula, contener solo letras, dígitos o guion, y tener máximo 31 caracteres.");
        RuleFor(c => c.Name).NotEmpty().MaximumLength(255);
        RuleFor(c => c.Description).MaximumLength(2000);
        RuleFor(c => c.Color).MaximumLength(32);
        RuleFor(c => c.Icon).MaximumLength(64);
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{0,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectSlugRegex();
}

internal sealed class CreateProjectHandler(ProjectsDbContext db, IClock clock)
    : ICommandHandler<CreateProjectCommand, ProjectDetail>
{
    public async Task<Result<ProjectDetail>> Handle(CreateProjectCommand request, CancellationToken cancellationToken)
    {
        var slugResult = Slug.Create(request.Slug);
        if (slugResult.IsFailure)
        {
            return slugResult.Error;
        }

        var slug = slugResult.Value;
        if (await db.Projects.AnyAsync(p => p.Slug == slug, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict(
                "project.slug_taken",
                $"Ya existe un proyecto con slug '{slug.Value}'.");
        }

        Project project;
        try
        {
            project = Project.Create(slug, request.Name, clock.UtcNow, request.Description, request.Color, request.Icon);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("project.invalid", ex.Message);
        }

        db.Projects.Add(project);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ProjectDetail(
            id: project.Id.ToString(),
            slug: project.Slug.Value,
            name: project.Name,
            description: project.Description,
            color: project.Color,
            icon: project.Icon,
            templateCount: 0,
            clientCount: 0,
            createdAt: project.CreatedAt,
            updatedAt: project.UpdatedAt);
    }
}
