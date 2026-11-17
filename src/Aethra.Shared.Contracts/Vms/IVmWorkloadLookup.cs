namespace Aethra.Shared.Contracts.Vms;

/// <summary>
/// Referencia mínima a una carga de trabajo alojada en una VM (instancia de proyecto, servicio gestionado, conector de túnel, etc.).
/// </summary>
/// <param name="Kind">Tipo de carga ("instance", "managed_service", "tunnel", etc.).</param>
/// <param name="Id">Identificador único de la carga.</param>
/// <param name="Name">Nombre representativo o contenedor de la carga.</param>
public sealed record VmWorkloadRef(string Kind, string Id, string Name);

/// <summary>
/// Consulta cross-module que lista las cargas asociadas a una VM específica.
/// Cada módulo dueño de cargas (Projects, Services, Cloudflare) implementa esta interfaz.
/// </summary>
public interface IVmWorkloadLookup
{
    Task<IReadOnlyList<VmWorkloadRef>> ListOnVmAsync(string vmId, CancellationToken ct);
}
