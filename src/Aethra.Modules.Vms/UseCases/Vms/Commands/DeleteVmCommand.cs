using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Vms.UseCases.Vms.Commands;

/// <summary>
/// Borra una VM del registro de Aethra (junto con su satélite/token).
///
/// nota: el módulo Vms no comparte DbContext con Projects (Instances) ni con Services
/// (ManagedServices), así que no podemos verificar acá si la VM tiene cargas activas. El
/// <paramref name="Force"/> se acepta por simetría de API, pero la limpieza de contenedores /
/// instancias que apuntaban a esta VM es responsabilidad del caller (debe reasignar o borrar
/// esas cargas antes de invocar esto).
/// </summary>
public sealed record DeleteVmCommand(string VmId, bool Force = false) : ICommand;

internal sealed class DeleteVmHandler(VmsDbContext db)
    : ICommandHandler<DeleteVmCommand>
{
    public async Task<Result> Handle(DeleteVmCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.VmId, out var parsed) || parsed.Value.Prefix != "vm")
        {
            return Error.Validation("vm.invalid_id", "ID de VM inválido.");
        }
        var typedId = new VmId(parsed.Value);
        var vm = await db.Vms.FirstOrDefaultAsync(v => v.Id == typedId, cancellationToken).ConfigureAwait(false);
        if (vm is null)
        {
            return Error.NotFound("vm.not_found", $"VM '{request.VmId}' no existe.");
        }

        // nota: sin guard cross-module — ver doc del command. Force solo existe por simetría.
        _ = request.Force;

        db.Vms.Remove(vm);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
