using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.UseCases.Dtos;
using Aethra.Modules.Services.UseCases.Mapping;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Services.UseCases.Queries;

public sealed record ListBindingsQuery(string ServiceId, bool IncludeRevoked = false)
    : IQuery<IReadOnlyList<ServiceBindingDto>>;

internal sealed class ListBindingsHandler(ServicesDbContext db, IInstanceLookup instanceLookup)
    : IQueryHandler<ListBindingsQuery, IReadOnlyList<ServiceBindingDto>>
{
    public async Task<Result<IReadOnlyList<ServiceBindingDto>>> Handle(ListBindingsQuery request, CancellationToken cancellationToken)
    {
        // Comparamos por el wrapper tipado (ManagedServiceId) que SI traduce a SQL con el
        // ValueConverter activo. Eso evita materializar toda la tabla en memoria.
        if (!AethraId.TryParse(request.ServiceId, out var parsed) || parsed.Value.Prefix != "svc")
        {
            return Result.Success<IReadOnlyList<ServiceBindingDto>>(Array.Empty<ServiceBindingDto>());
        }
        var typedServiceId = new ManagedServiceId(parsed.Value);

        var bindingsQuery = db.ServiceBindings.AsNoTracking()
            .Where(b => b.ServiceId == typedServiceId);
        if (!request.IncludeRevoked)
        {
            bindingsQuery = bindingsQuery.Where(b => b.RevokedAt == null);
        }
        var bindings = await bindingsQuery
            .OrderBy(b => b.CreatedAt)
            .ToListAsync(cancellationToken);

        // Enriquecer con slug (lookup por instance). Pocas bindings esperadas → N+1 aceptable en MVP.
        var dtos = new List<ServiceBindingDto>(bindings.Count);
        foreach (var b in bindings)
        {
            var instance = await instanceLookup.GetByIdAsync(b.InstanceId, cancellationToken);
            dtos.Add(ServiceMappers.ToDto(b, instance?.Slug));
        }
        return Result.Success<IReadOnlyList<ServiceBindingDto>>(dtos);
    }
}
