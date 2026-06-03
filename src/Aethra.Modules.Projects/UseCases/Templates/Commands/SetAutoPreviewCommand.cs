using Aethra.Modules.Projects.Domain.Templates;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Templates.Commands;

/// <summary>
/// F12.3 — toggle del flag <c>Template.AutoPreviewPullRequests</c>.
/// </summary>
public sealed record SetAutoPreviewCommand(string TemplateId, bool Enabled) : ICommand;

internal sealed class SetAutoPreviewHandler(ProjectsDbContext db, IClock clock)
    : ICommandHandler<SetAutoPreviewCommand>
{
    public async Task<Result> Handle(SetAutoPreviewCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.TemplateId, out var parsed) || parsed.Value.Prefix != "tpl")
        {
            return Error.Validation("template.invalid_id", "ID de template inválido.");
        }
        var typedId = new TemplateId(parsed.Value);
        var template = await db.Templates
            .FirstOrDefaultAsync(t => t.Id == typedId, cancellationToken)
            .ConfigureAwait(false);
        if (template is null)
        {
            return Error.NotFound("template.not_found", $"Template '{request.TemplateId}' no existe.");
        }
        template.SetAutoPreviewPullRequests(request.Enabled, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
