using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.Infrastructure;
using Aethra.Modules.Vms.UseCases.Dtos;
using Aethra.Modules.Vms.UseCases.Vms;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Vms.UseCases.Vms.Queries;

public sealed record GetVmByIdQuery(string VmId) : IQuery<VmDto>;

internal sealed class GetVmByIdHandler(VmsDbContext db) : IQueryHandler<GetVmByIdQuery, VmDto>
{
    public async Task<Result<VmDto>> Handle(GetVmByIdQuery request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.VmId, out var parsed) || parsed.Value.Prefix != "vm")
        {
            return Error.Validation("vm.invalid_id", "ID de VM inválido.");
        }
        var typedId = new VmId(parsed.Value);

        var vm = await db.Vms.AsNoTracking().FirstOrDefaultAsync(v => v.Id == typedId, ct);
        if (vm is null)
        {
            return Error.NotFound("vm.not_found", $"No existe la VM '{request.VmId}'.");
        }
        return VmMapper.ToDto(vm);
    }
}
