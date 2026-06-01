using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.UseCases.Dtos;
using Aethra.Modules.Services.UseCases.Mapping;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Infrastructure.Cqrs;
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
        // EF Core 10 no traduce `Id.ToString() == arg` con ValueConverter activo.
        var allBindings = await db.ServiceBindings.AsNoTracking().ToListAsync(cancellationToken);
        IEnumerable<Aethra.Modules.Services.Domain.ServiceBinding> filtered =
            allBindings.Where(b => b.ServiceId.ToString() == request.ServiceId);
        if (!request.IncludeRevoked)
        {
            filtered = filtered.Where(b => b.RevokedAt == null);
        }
        var bindings = filtered.OrderBy(b => b.CreatedAt).ToList();

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
