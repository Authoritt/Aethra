using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Projects.Commands;

/// <summary>
/// Actualiza nombre y apariencia (descripción, color, icono) de un <c>Project</c>. El slug NO cambia
/// (es único globalmente y compone nombres). Reutiliza <c>Project.Rename</c> + <c>UpdateAppearance</c>.
/// </summary>
public sealed record UpdateProjectCommand(
    string ProjectId,
    string Name,
    string? Description,
    string? Color,
    string? Icon) : ICommand;

public sealed class UpdateProjectValidator : AbstractValidator<UpdateProjectCommand>
{
    public UpdateProjectValidator()
    {
        RuleFor(c => c.ProjectId).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(255);
        RuleFor(c => c.Description).MaximumLength(2000);
        RuleFor(c => c.Color).MaximumLength(32);
        RuleFor(c => c.Icon).MaximumLength(64);
    }
}

internal sealed class UpdateProjectHandler(ProjectsDbContext db, IClock clock)
    : ICommandHandler<UpdateProjectCommand>
{
    public async Task<Result> Handle(UpdateProjectCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.ProjectId, out var parsed) || parsed.Value.Prefix != "prj")
        {
            return Error.Validation("project.invalid_id", "ID de proyecto inválido.");
        }
        var projectId = new ProjectId(parsed.Value);

        var project = await db.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            .ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound("project.not_found", $"Proyecto '{request.ProjectId}' no existe.");
        }

        try
        {
            project.Rename(request.Name, clock.UtcNow);
            project.UpdateAppearance(request.Description, request.Color, request.Icon, clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("project.invalid", ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
