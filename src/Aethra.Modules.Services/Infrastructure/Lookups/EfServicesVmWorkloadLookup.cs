using Aethra.Shared.Contracts.Vms;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Services.Infrastructure.Lookups;

/// <summary>
/// Implementación de <see cref="IVmWorkloadLookup"/> para el módulo Services.
/// Lista los servicios gestionados alojados en una VM determinada.
/// </summary>
internal sealed class EfServicesVmWorkloadLookup(ServicesDbContext db) : IVmWorkloadLookup
{
    public async Task<IReadOnlyList<VmWorkloadRef>> ListOnVmAsync(string vmId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(vmId))
        {
            return [];
        }

        var services = await db.ManagedServices
            .AsNoTracking()
            .Where(s => s.TargetVmId == vmId)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return services
            .Select(s => new VmWorkloadRef("managed_service", s.Id.ToString(), s.Name))
            .ToList();
    }
}
