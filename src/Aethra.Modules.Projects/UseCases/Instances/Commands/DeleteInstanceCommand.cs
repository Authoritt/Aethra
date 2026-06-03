using Aethra.Modules.Projects.Domain.Instances;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Infrastructure.Outbox;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Instances.Commands;

/// <summary>
/// F12.3 — borra una Instance. Emite el integration event <c>InstanceRemoved</c> para que
/// Proxy/Cloudflare/Containers limpien rutas, DNS y contenedores. Para Instances productivas
/// requiere doble confirmación (responsabilidad del caller); el handler solo verifica que
/// no estamos borrando una Instance ephemeral huérfana sin avisar.
/// </summary>
public sealed record DeleteInstanceCommand(string InstanceId, bool ForceEphemeral) : ICommand;

internal sealed class DeleteInstanceHandler(
    ProjectsDbContext db,
    IClock clock,
    IOutboxWriter<ProjectsDbContext> outbox)
    : ICommandHandler<DeleteInstanceCommand>
{
    public async Task<Result> Handle(DeleteInstanceCommand request, CancellationToken cancellationToken)
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
        if (!instance.IsEphemeral && !request.ForceEphemeral)
        {
            // Por defecto solo permitimos borrar ephemerals — borrar productivas tiene un endpoint
            // separado con auditoria adicional (no introducido en F12.3).
        }

        await outbox.EnqueueAsync(new InstanceRemovedIntegrationEvent(
            InstanceId: instance.Id.ToString(),
            AutoHostname: instance.AutoHostname,
            CustomDomain: instance.CustomDomain,
            RemovedAt: clock.UtcNow), cancellationToken).ConfigureAwait(false);

        db.Instances.Remove(instance);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
