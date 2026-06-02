using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
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
        // Comparamos por el wrapper tipado (ManagedServiceId) que SI traduce a SQL con el
        // ValueConverter activo. Eso evita materializar toda la tabla en memoria.
        if (!AethraId.TryParse(request.ServiceId, out var parsed) || parsed.Value.Prefix != "svc")
        {
            return Error.NotFound("service.not_found", $"ManagedService '{request.ServiceId}' no existe.");
        }
        var typedId = new ManagedServiceId(parsed.Value);

        var svc = await db.ManagedServices.FirstOrDefaultAsync(s => s.Id == typedId, cancellationToken);
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
