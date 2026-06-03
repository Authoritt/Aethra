namespace Aethra.Shared.Contracts.Vms;

/// <summary>
/// F12.3 — Read-model cross-module: lista las VMs disponibles como targets para previews.
/// La implementación filtra por <c>AcceptsPreviews=true</c> y opcionalmente <c>Status=Connected</c>.
///
/// El módulo <c>Projects</c> (<see cref="Aethra.Shared.Contracts.Projects.IPreviewInstanceCoordinator"/>)
/// lo usa para hacer round-robin al crear una Instance ephemeral.
/// </summary>
public interface IPreviewVmPool
{
    /// <summary>
    /// Devuelve los <c>VmId</c>s elegibles para hospedar una preview. Lista ordenada por
    /// <c>Id</c> ascendente para que el round-robin sea deterministic dado el módulo de N.
    /// Vacío si no hay VMs configuradas con <c>AcceptsPreviews=true</c>.
    /// </summary>
    Task<IReadOnlyList<string>> ListAvailableVmIdsAsync(CancellationToken ct);
}
