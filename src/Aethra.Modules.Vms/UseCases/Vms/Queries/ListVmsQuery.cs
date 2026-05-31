using Aethra.Modules.Vms.Infrastructure;
using Aethra.Modules.Vms.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Vms.UseCases.Vms.Queries;

public sealed record ListVmsQuery : IQuery<IReadOnlyList<VmDto>>;

internal sealed class ListVmsHandler(VmsDbContext db) : IQueryHandler<ListVmsQuery, IReadOnlyList<VmDto>>
{
    public async Task<Result<IReadOnlyList<VmDto>>> Handle(ListVmsQuery request, CancellationToken ct)
    {
        var vms = await db.Vms.AsNoTracking().OrderBy(v => v.Slug).ToListAsync(ct);
        return Result.Success<IReadOnlyList<VmDto>>(vms.Select(VmMapper.ToDto).ToList());
    }
}
