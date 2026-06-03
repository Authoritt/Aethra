using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Projects.Commands;

/// <summary>
/// F12.3 — actualiza el cap de previews concurrentes del Project. <c>0</c> deshabilita previews.
/// </summary>
public sealed record SetPreviewConfigCommand(string ProjectId, int PreviewMaxConcurrent) : ICommand;

internal sealed class SetPreviewConfigHandler(ProjectsDbContext db, IClock clock)
    : ICommandHandler<SetPreviewConfigCommand>
{
    public async Task<Result> Handle(SetPreviewConfigCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.ProjectId, out var parsed) || parsed.Value.Prefix != "prj")
        {
            return Error.Validation("project.invalid_id", "ID de proyecto inválido.");
        }
        if (request.PreviewMaxConcurrent < 0 || request.PreviewMaxConcurrent > 1000)
        {
            return Error.Validation("project.invalid_preview_quota", "PreviewMaxConcurrent debe ser 0..1000.");
        }
        var typedId = new ProjectId(parsed.Value);
        var project = await db.Projects
            .FirstOrDefaultAsync(p => p.Id == typedId, cancellationToken)
            .ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound("project.not_found", $"Project '{request.ProjectId}' no existe.");
        }
        project.SetPreviewMaxConcurrent(request.PreviewMaxConcurrent, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
