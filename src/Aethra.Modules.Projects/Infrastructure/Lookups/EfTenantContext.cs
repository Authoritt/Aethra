using Aethra.Shared.Contracts.Projects;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.Infrastructure.Lookups;

/// <summary>
/// Implementación EF Core de <see cref="ITenantContext"/>. Resuelve el <c>ClientId</c>
/// asociado a una Instance en una sola query proyectada.
///
/// Decisión: NO se delega en <see cref="IInstanceLookup"/> para evitar materializar la
/// entidad completa cuando solo se necesita el FK — los módulos consumidores
/// (Monitoring, Cloudflare) lo invocan en hot-paths y prefieren la proyección directa.
/// </summary>
internal sealed class EfTenantContext(ProjectsDbContext db) : ITenantContext
{
    public async Task<string?> GetClientIdForInstanceAsync(string instanceId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(instanceId);
        // EF Core 10 no traduce `Id.ToString() == arg` con ValueConverter activo.
        // Materializamos solo (Id, ClientId) y resolvemos en memoria.
        var pairs = await db.Instances.AsNoTracking()
            .Select(i => new { i.Id, i.ClientId })
            .ToListAsync(ct).ConfigureAwait(false);
        var match = pairs.FirstOrDefault(p => p.Id.ToString() == instanceId);
        return match is null ? null : match.ClientId.ToString();
    }
}
