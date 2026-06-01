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
        // ClientId tiene un ValueConverter a string, por eso proyectamos al string ya convertido
        // dejando que EF haga la traducción en SQL. Si la Instance no existe, FirstOrDefaultAsync
        // devuelve null (string default).
        return await db.Instances
            .AsNoTracking()
            .Where(i => i.Id.ToString() == instanceId)
            .Select(i => i.ClientId.ToString())
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }
}
