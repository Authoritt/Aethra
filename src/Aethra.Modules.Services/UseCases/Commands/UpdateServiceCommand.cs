using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.UseCases.Dtos;
using Aethra.Modules.Services.UseCases.Mapping;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Services.UseCases.Commands;

/// <summary>
/// Actualiza metadata editable de un <c>ManagedService</c>: nombre display y exposición externa.
/// Slug/imagen/puerto/VM son inmutables (atados al contenedor). Devuelve el detalle actualizado.
/// </summary>
public sealed record UpdateServiceCommand(string ServiceId, string Name, bool ExposedExternally)
    : ICommand<ManagedServiceDetailDto>;

internal sealed class UpdateServiceHandler(ServicesDbContext db, IClock clock)
    : ICommandHandler<UpdateServiceCommand, ManagedServiceDetailDto>
{
    public async Task<Result<ManagedServiceDetailDto>> Handle(UpdateServiceCommand request, CancellationToken cancellationToken)
    {
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

        try
        {
            svc.UpdateInfo(request.Name, request.ExposedExternally, clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("service.invalid", ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);

        var bindings = await db.ServiceBindings.AsNoTracking()
            .CountAsync(b => b.ServiceId == svc.Id && b.RevokedAt == null, cancellationToken);
        return ServiceMappers.ToDetail(svc, bindings);
    }
}
