using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.UseCases.Dtos;
using Aethra.Modules.Services.UseCases.Mapping;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Services.UseCases.Queries;

public sealed record GetServiceByIdQuery(string ServiceId) : IQuery<ManagedServiceDetailDto>;

internal sealed class GetServiceByIdHandler(ServicesDbContext db)
    : IQueryHandler<GetServiceByIdQuery, ManagedServiceDetailDto>
{
    public async Task<Result<ManagedServiceDetailDto>> Handle(GetServiceByIdQuery request, CancellationToken cancellationToken)
    {
        // EF Core 10 no traduce `Id.ToString() == arg` con ValueConverter activo.
        var allSvcs = await db.ManagedServices.AsNoTracking().ToListAsync(cancellationToken);
        var svc = allSvcs.FirstOrDefault(s => s.Id.ToString() == request.ServiceId);
        if (svc is null)
        {
            return Error.NotFound("service.not_found", $"ManagedService '{request.ServiceId}' no existe.");
        }
        var bindings = await db.ServiceBindings.AsNoTracking()
            .CountAsync(b => b.ServiceId == svc.Id && b.RevokedAt == null, cancellationToken);
        return ServiceMappers.ToDetail(svc, bindings);
    }
}
