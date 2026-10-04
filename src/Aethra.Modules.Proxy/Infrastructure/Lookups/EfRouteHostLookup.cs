using Aethra.Shared.Contracts.Proxy;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Proxy.Infrastructure.Lookups;

/// <summary>
/// OT-009 (G2) — implementación EF de <see cref="IRouteHostLookup"/>. La tabla de rutas es chica
/// (decenas de filas): se materializa y se filtra en memoria, como <c>InstanceRemovedHandler</c>,
/// para no traducir el value object <c>Hostname</c> a SQL.
/// </summary>
internal sealed class EfRouteHostLookup(ProxyDbContext db) : IRouteHostLookup
{
    public async Task<IReadOnlyList<RouteHostOwnerView>> FindByHostnameAsync(string hostname, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            return [];
        }
        var host = hostname.Trim();
        var routes = await db.Routes.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        return [.. routes
            .Where(r => string.Equals(r.Hostname.Value, host, StringComparison.OrdinalIgnoreCase))
            .Select(r => new RouteHostOwnerView(r.Hostname.Value, r.PathPrefix, r.OperationalOwnerId))];
    }
}
