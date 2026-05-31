using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Primitives;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Projects.Commands;

/// <summary>
/// Crea un Project con su <c>Environment</c> default. El slug se valida o se sugiere desde el nombre.
/// </summary>
public sealed record CreateProjectCommand(
    string Name,
    string? Slug = null,
    string? Description = null,
    string? Color = null,
    string? Icon = null,
    string DefaultEnvironment = "production") : ICommand<ProjectDto>;

public sealed class CreateProjectValidator : AbstractValidator<CreateProjectCommand>
{
    public CreateProjectValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(255);
        RuleFor(c => c.DefaultEnvironment).NotEmpty().MaximumLength(64);
    }
}

internal sealed class CreateProjectHandler(ProjectsDbContext db, IClock clock)
    : ICommandHandler<CreateProjectCommand, ProjectDto>
{
    public async Task<Result<ProjectDto>> Handle(CreateProjectCommand request, CancellationToken cancellationToken)
    {
        var slugResult = request.Slug is { Length: > 0 }
            ? Slug.Create(request.Slug)
            : Slug.Suggest(request.Name);
        if (slugResult.IsFailure)
        {
            return slugResult.Error;
        }
        var slug = slugResult.Value;

        var slugExists = await db.Projects.AnyAsync(p => p.Slug == slug, cancellationToken).ConfigureAwait(false);
        if (slugExists)
        {
            return Error.Conflict("project.slug_taken", $"Ya existe un proyecto con slug '{slug}'.");
        }

        var project = Project.Create(slug, request.Name, clock.UtcNow, request.Description, request.Color, request.Icon,
            request.DefaultEnvironment);

        db.Projects.Add(project);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ProjectMapper.ToDto(project);
    }
}

// Helper de Slug.Suggest sin Result (la sugerencia no falla):
file static class SlugExtensions
{
    public static Result<Slug> ToResult(this Slug slug) => slug;
}
