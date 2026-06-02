using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.UseCases.Dtos;
using Aethra.Modules.Services.UseCases.Mapping;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Services.UseCases.Queries;

public sealed record GetServiceByIdQuery(string ServiceId) : IQuery<ManagedServiceDetailDto>;

internal sealed class GetServiceByIdHandler(ServicesDbContext db)
    : IQueryHandler<GetServiceByIdQuery, ManagedServiceDetailDto>
{
    public async Task<Result<ManagedServiceDetailDto>> Handle(GetServiceByIdQuery request, CancellationToken cancellationToken)
    {
        // Comparamos por el wrapper tipado (ManagedServiceId) que SI traduce a SQL con el
        // ValueConverter activo. Eso evita materializar toda la tabla en memoria.
        if (!AethraId.TryParse(request.ServiceId, out var parsed) || parsed.Value.Prefix != "svc")
        {
            return Error.NotFound("service.not_found", $"ManagedService '{request.ServiceId}' no existe.");
        }
        var typedId = new ManagedServiceId(parsed.Value);

        var svc = await db.ManagedServices.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == typedId, cancellationToken);
        if (svc is null)
        {
            return Error.NotFound("service.not_found", $"ManagedService '{request.ServiceId}' no existe.");
        }
        var bindings = await db.ServiceBindings.AsNoTracking()
            .CountAsync(b => b.ServiceId == svc.Id && b.RevokedAt == null, cancellationToken);
        return ServiceMappers.ToDetail(svc, bindings);
    }
}
