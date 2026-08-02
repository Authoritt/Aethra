using Aethra.Modules.Vms.UseCases.Vms.Queries;
using Aethra.Shared.Contracts.Containers;
using MediatR;

namespace Aethra.Api.Hubs;

/// <summary>
/// Implementación de <see cref="ISatelliteCapacityProvider"/> en el host central: lee el read model
/// de VMs (<see cref="ListVmsQuery"/>) para la capacidad, y el registro de conexiones VIVO para
/// saber quién está realmente ahí. Vive en <c>apps/api</c> (no en un módulo) porque cruza el módulo
/// Vms con el contrato compartido que consumen otros módulos (p.ej. el backend de backup
/// satellite://).
///
/// <para>
/// <b>Por qué la presencia NO sale de la BD.</b> Antes, <c>Connected</c> se derivaba de la columna
/// <c>Status</c>: <c>string.Equals(v.Status, "Connected")</c>. Esa columna solo pasa a Disconnected
/// cuando SignalR observa una desconexión limpia (<c>SatelliteHub.OnDisconnectedAsync</c>); si el
/// satélite muere, la máquina se apaga o la red se parte, se queda en Connected indefinidamente.
/// </para>
///
/// <para>
/// Y lo consumen dos planificadores: el de builds elige el Connected con MÁS memoria, el de backups
/// el Connected con más disco. Los dos mandarían trabajo a una máquina que no existe. Peor:
/// <c>BuildOrchestrator</c> ya tenía un fallback que usaba este mismo registro vivo, pero solo corre
/// cuando la vía principal no encuentra nada — así que el valor obsoleto no engañaba, se ADELANTABA
/// a la comprobación correcta que tenía justo debajo.
/// </para>
///
/// <para>
/// La capacidad (disco, CPU, memoria) sí sigue viniendo del read model: es el último dato reportado
/// y no hay otro. Pero un dato de capacidad viejo de una máquina viva solo desordena la preferencia;
/// un flag de presencia viejo manda trabajo al vacío. Ver issue #19.
/// </para>
/// </summary>
public sealed class MediatrSatelliteCapacityProvider(
    IMediator mediator,
    ISatelliteConnectionRegistry registry) : ISatelliteCapacityProvider
{
    public async Task<IReadOnlyList<SatelliteCapacity>> GetSatellitesAsync(CancellationToken ct)
    {
        var result = await mediator.Send(new ListVmsQuery(), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return [];
        }

        // Un solo snapshot: consultando por VM, una conexión que cae a mitad de la proyección
        // produciría una lista internamente inconsistente.
        var vivos = registry.ConnectedVmIds.ToHashSet(StringComparer.Ordinal);

        return result.Value
            .Select(v => new SatelliteCapacity(
                v.Id,
                v.Slug,
                v.RootDiskAvailableBytes,
                vivos.Contains(v.Id),
                v.CpuCores,
                v.TotalMemoryBytes))
            .ToList();
    }
}
