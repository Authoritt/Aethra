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

internal sealed class ListBindingsHandler(ServicesDbContext db, IApplicationLookup appLookup)
    : IQueryHandler<ListBindingsQuery, IReadOnlyList<ServiceBindingDto>>
{
    public async Task<Result<IReadOnlyList<ServiceBindingDto>>> Handle(ListBindingsQuery request, CancellationToken cancellationToken)
    {
        var q = db.ServiceBindings.AsNoTracking()
            .Where(b => b.ServiceId.ToString() == request.ServiceId);
        if (!request.IncludeRevoked)
        {
            q = q.Where(b => b.RevokedAt == null);
        }
        var bindings = await q.OrderBy(b => b.CreatedAt).ToListAsync(cancellationToken);

        // Enriquecer con slug (lookup por app). Pocas bindings esperadas → N+1 aceptable en MVP.
        var dtos = new List<ServiceBindingDto>(bindings.Count);
        foreach (var b in bindings)
        {
            var app = await appLookup.GetByIdAsync(b.ApplicationId, cancellationToken);
            dtos.Add(ServiceMappers.ToDto(b, app?.Slug));
        }
        return Result.Success<IReadOnlyList<ServiceBindingDto>>(dtos);
    }
}
