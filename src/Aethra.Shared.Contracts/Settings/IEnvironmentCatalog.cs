namespace Aethra.Shared.Contracts.Settings;

/// <summary>
/// Read-model cross-module: catálogo de ambientes válidos. Permite que otros módulos
/// (Projects, Deployments) validen el slug de ambiente sin duplicar la lista canónica.
///
/// Orden estable (configurable por el usuario, ver <c>ReorderEnvironmentDefinitionsCommand</c>):
/// la UI los muestra en este orden, y el orden refleja la progresión natural
/// (preview → test → staging → production).
/// </summary>
public interface IEnvironmentCatalog
{
    /// <summary>
    /// Lista todos los slugs ordenados ascendentemente por el campo <c>Order</c>.
    /// </summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Indica si <paramref name="slug"/> es un ambiente válido. Comparación
    /// case-insensitive (ordinal) — los slugs son lowercase por convención.
    /// </summary>
    Task<bool> IsValidAsync(string slug, CancellationToken ct);
}
