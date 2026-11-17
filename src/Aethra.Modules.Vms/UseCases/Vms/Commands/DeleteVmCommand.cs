using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.Infrastructure;
using Aethra.Shared.Contracts.Containers;
using Aethra.Shared.Contracts.Vms;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Vms.UseCases.Vms.Commands;

/// <summary>
/// Borra una VM del registro de Aethra (junto con su satélite/token).
///
/// Reglas de protección de borrado:
/// 1. Si la VM tiene cargas asignadas (instancias de proyectos, servicios gestionados o túneles de Cloudflare),
///    se rechaza con error de conflicto (<c>vm.in_use</c>) tanto si <paramref name="Force"/> es true como false.
///    El operador debe borrar o mover las cargas antes de eliminar la VM.
/// 2. Si la VM no tiene cargas pero tiene un satélite conectado activamente y <paramref name="Force"/> es false,
///    se rechaza con error de conflicto (<c>vm.satellite_connected</c>).
/// 3. Si no tiene cargas (o si tiene satélite conectado con <paramref name="Force"/>=true), se borra la VM. Al eliminarse
///    el registro de la base de datos, el token del satélite queda revocado y cualquier satélite conectado caerá en su
///    próximo intento de reconexión o petición autenticada.
/// </summary>
public sealed record DeleteVmCommand(string VmId, bool Force = false) : ICommand;

internal sealed class DeleteVmHandler(
    VmsDbContext db,
    IEnumerable<IVmWorkloadLookup>? workloadLookups = null,
    ISatelliteConnectionRegistry? satelliteRegistry = null)
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

        // 1. Regla: comprobar si la VM tiene cargas activas asignadas (instancias, servicios, túneles, etc.).
        // Si tiene cargas, se rechaza SIEMPRE (con o sin force) con vm.in_use.
        var workloads = new List<VmWorkloadRef>();
        if (workloadLookups is not null)
        {
            foreach (var lookup in workloadLookups)
            {
                var items = await lookup.ListOnVmAsync(request.VmId, cancellationToken).ConfigureAwait(false);
                if (items is { Count: > 0 })
                {
                    workloads.AddRange(items);
                }
            }
        }

        if (workloads.Count > 0)
        {
            const int maxListed = 10;
            var sample = workloads.Take(maxListed).Select(w => $"{w.Kind}:{w.Name}");
            var summary = string.Join(", ", sample);
            var detail = workloads.Count > maxListed
                ? $"{summary} y {workloads.Count - maxListed} más"
                : summary;

            return Error.Conflict("vm.in_use",
                $"No se puede borrar la VM '{request.VmId}' porque tiene {workloads.Count} carga(s) asignada(s): {detail}. Debe borrar o mover las cargas antes de eliminar la VM.");
        }

        // 2. Regla: sin cargas pero con el satélite conectado y Force == false -> vm.satellite_connected.
        if (!request.Force && satelliteRegistry is not null && satelliteRegistry.IsConnected(request.VmId))
        {
            return Error.Conflict("vm.satellite_connected",
                $"No se puede borrar la VM '{request.VmId}' porque tiene un satélite conectado. Pasa force=true para borrarla de todos modos.");
        }

        // 3. Con force y satélite conectado: al borrar la VM (db.Vms.Remove(vm)), su registro y token quedan
        // revocados. No existe API de cierre forzado de socket en ISatelliteConnectionRegistry; el satélite
        // caerá en su próximo intento de reconexión o petición autenticada.
        db.Vms.Remove(vm);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
