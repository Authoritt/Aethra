using Aethra.Modules.Services.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Services.UseCases.Commands;

public sealed record DeleteServiceCommand(string ServiceId) : ICommand;

internal sealed class DeleteServiceHandler(ServicesDbContext db, IClock clock)
    : ICommandHandler<DeleteServiceCommand>
{
    public async Task<Result> Handle(DeleteServiceCommand request, CancellationToken cancellationToken)
    {
        // EF Core 10 no traduce `Id.ToString() == arg` con ValueConverter activo.
        var allSvcs = await db.ManagedServices.ToListAsync(cancellationToken);
        var svc = allSvcs.FirstOrDefault(s => s.Id.ToString() == request.ServiceId);
        if (svc is null)
        {
            return Error.NotFound("service.not_found", $"ManagedService '{request.ServiceId}' no existe.");
        }
        var activeBindings = await db.ServiceBindings
            .CountAsync(b => b.ServiceId == svc.Id && b.RevokedAt == null, cancellationToken);
        if (activeBindings > 0)
        {
            return Error.Conflict("service.has_active_bindings",
                $"No se puede eliminar: hay {activeBindings} binding(s) activo(s). Revócalos primero.");
        }
        svc.MarkStopped(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
