using Aethra.Modules.Projects.Domain.Instances;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Instances.Commands;

/// <summary>
/// F12.3 — setea o limpia el <see cref="Instance.TrackedRef"/> de una Instance. <c>null</c> /
/// whitespace = vuelve a la cascada del Template.
/// </summary>
public sealed record SetTrackedRefCommand(string InstanceId, string? TrackedRef) : ICommand;

internal sealed class SetTrackedRefHandler(ProjectsDbContext db, IClock clock)
    : ICommandHandler<SetTrackedRefCommand>
{
    public async Task<Result> Handle(SetTrackedRefCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.InstanceId, out var parsed) || parsed.Value.Prefix != "ins")
        {
            return Error.Validation("instance.invalid_id", "ID de instance inválido.");
        }
        var typedId = new InstanceId(parsed.Value);
        var instance = await db.Instances
            .FirstOrDefaultAsync(i => i.Id == typedId, cancellationToken)
            .ConfigureAwait(false);
        if (instance is null)
        {
            return Error.NotFound("instance.not_found", $"Instance '{request.InstanceId}' no existe.");
        }
        if (instance.IsEphemeral)
        {
            return Error.Validation("instance.ephemeral_no_override",
                "No se puede cambiar el TrackedRef de una Instance ephemeral (gestionada por PR).");
        }
        instance.SetTrackedRef(request.TrackedRef, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
