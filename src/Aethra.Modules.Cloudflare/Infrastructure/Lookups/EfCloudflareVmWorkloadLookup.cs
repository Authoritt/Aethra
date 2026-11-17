using Aethra.Shared.Contracts.Vms;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Cloudflare.Infrastructure.Lookups;

/// <summary>
/// Implementación de <see cref="IVmWorkloadLookup"/> para el módulo Cloudflare.
/// Lista los túneles cuyo conector está asignado a una VM determinada.
/// </summary>
public sealed class EfCloudflareVmWorkloadLookup(CloudflareDbContext db) : IVmWorkloadLookup
{
    public async Task<IReadOnlyList<VmWorkloadRef>> ListOnVmAsync(string vmId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(vmId))
        {
            return [];
        }

        var tunnels = await db.Tunnels
            .AsNoTracking()
            .Where(t => t.TargetVmId == vmId)
            .Select(t => new { t.Id, t.Name })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return tunnels
            .Select(t => new VmWorkloadRef("tunnel", t.Id.ToString(), t.Name))
            .ToList();
    }
}
