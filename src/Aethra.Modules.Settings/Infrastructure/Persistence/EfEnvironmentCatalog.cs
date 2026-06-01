using Aethra.Shared.Contracts.Settings;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// Implementación EF de <see cref="IEnvironmentCatalog"/>. Lee la tabla
/// <c>environment_definitions</c> ordenada ascendentemente por <c>Order</c>.
/// </summary>
internal sealed class EfEnvironmentCatalog(SettingsDbContext db) : IEnvironmentCatalog
{
    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken ct)
    {
        var slugs = await db.EnvironmentDefinitions
            .AsNoTracking()
            .OrderBy(e => e.Order)
            .ThenBy(e => e.Slug)
            .Select(e => e.Slug)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return slugs;
    }

    public async Task<bool> IsValidAsync(string slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return false;
        }
        var normalized = slug.Trim().ToLowerInvariant();
        return await db.EnvironmentDefinitions
            .AsNoTracking()
            .AnyAsync(e => e.Slug == normalized, ct)
            .ConfigureAwait(false);
    }
}
