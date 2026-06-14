using Aethra.Modules.Vms.UseCases.Vms.Queries;
using Aethra.Shared.Contracts.Containers;
using MediatR;

namespace Aethra.Api.Hubs;

/// <summary>
/// Implementación de <see cref="ISatelliteCapacityProvider"/> en el host central: lee el read model
/// de VMs (<see cref="ListVmsQuery"/>) y proyecta la capacidad de disco de cada satélite. Vive en
/// <c>apps/api</c> (no en un módulo) porque cruza el módulo Vms con el contrato compartido que
/// consumen otros módulos (p.ej. el backend de backup satellite://).
/// </summary>
public sealed class MediatrSatelliteCapacityProvider(IMediator mediator) : ISatelliteCapacityProvider
{
    public async Task<IReadOnlyList<SatelliteCapacity>> GetSatellitesAsync(CancellationToken ct)
    {
        var result = await mediator.Send(new ListVmsQuery(), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return [];
        }
        return result.Value
            .Select(v => new SatelliteCapacity(
                v.Id,
                v.Slug,
                v.RootDiskAvailableBytes,
                string.Equals(v.Status, "Connected", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
