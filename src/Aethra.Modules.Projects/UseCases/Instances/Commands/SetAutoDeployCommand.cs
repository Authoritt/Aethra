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
/// Habilita/deshabilita el auto-deploy-on-new-build de una Instance. Lo consume el toggle de la UI
/// (POST /api/instances/{id}/auto-deploy/enable|disable).
/// </summary>
public sealed record SetAutoDeployCommand(string InstanceId, bool Enabled) : ICommand;

internal sealed class SetAutoDeployHandler(ProjectsDbContext db, IClock clock) : ICommandHandler<SetAutoDeployCommand>
{
    public async Task<Result> Handle(SetAutoDeployCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.InstanceId, out var parsed) || parsed.Value.Prefix != "ins")
        {
            return Error.Validation("instance.invalid_id", "ID de instance inválido.");
        }
        var instanceId = new InstanceId(parsed.Value);

        var instance = await db.Instances.FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken).ConfigureAwait(false);
        if (instance is null)
        {
            return Error.NotFound("instance.not_found", $"Instance '{request.InstanceId}' no existe.");
        }

        if (request.Enabled)
        {
            instance.EnableAutoDeploy(clock.UtcNow);
        }
        else
        {
            instance.DisableAutoDeploy(clock.UtcNow);
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
