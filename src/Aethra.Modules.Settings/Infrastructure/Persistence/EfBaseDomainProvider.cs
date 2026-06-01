using Aethra.Shared.Contracts.Settings;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// Implementación EF de <see cref="IBaseDomainProvider"/>. Devuelve la única instancia
/// con <c>IsActive=true</c>; si hay más de una (invariante rota) toma la primera por
/// orden estable de creación para que el sistema no quede en deadlock visual.
/// </summary>
internal sealed class EfBaseDomainProvider(SettingsDbContext db) : IBaseDomainProvider
{
    public async Task<BaseDomainInfo?> GetActiveAsync(CancellationToken ct)
    {
        var active = await db.BaseDomains
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.CreatedAt)
            .Select(d => new { d.Hostname, d.CloudflareZoneId, d.WildcardConfigured })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return active is null
            ? null
            : new BaseDomainInfo(active.Hostname, active.CloudflareZoneId, active.WildcardConfigured);
    }
}
