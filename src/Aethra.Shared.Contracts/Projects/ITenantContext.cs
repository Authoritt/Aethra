namespace Aethra.Shared.Contracts.Projects;

/// <summary>
/// Abstracción para resolver multi-tenant: dada una Instance, devuelve el Client al que pertenece.
///
/// Usada por módulos que necesitan auditoría/atribución sin acoplarse al modelo completo de
/// Projects (Monitoring, Cloudflare, Notes). La implementación se apoya en <see cref="IInstanceLookup"/>
/// internamente; se ofrece como interface separada porque el caso de uso "qué tenant es esto"
/// es muy frecuente y merece su propio contrato.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// Devuelve el <c>ClientId</c> asociado a una Instance, o <c>null</c> si la Instance no existe.
    /// </summary>
    Task<string?> GetClientIdForInstanceAsync(string instanceId, CancellationToken ct);
}
