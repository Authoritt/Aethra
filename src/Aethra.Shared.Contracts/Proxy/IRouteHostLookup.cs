namespace Aethra.Shared.Contracts.Proxy;

/// <summary>
/// OT-009 (G2) — lectura cross-module de las rutas del proxy por hostname, con su dueño operativo.
/// La usan Projects (no fijar un override sobre un host que sirve OTRA instancia) y Cloudflare
/// (no borrar el DNS de un host que otra ruta aún sirve).
/// </summary>
public interface IRouteHostLookup
{
    /// <summary>Rutas cuyo hostname es <paramref name="hostname"/> (case-insensitive).</summary>
    Task<IReadOnlyList<RouteHostOwnerView>> FindByHostnameAsync(string hostname, CancellationToken ct);
}

/// <param name="OperationalOwnerId">Instancia dueña de la ruta; null = ruta manual/sin dueño.</param>
public sealed record RouteHostOwnerView(string Hostname, string PathPrefix, string? OperationalOwnerId);
