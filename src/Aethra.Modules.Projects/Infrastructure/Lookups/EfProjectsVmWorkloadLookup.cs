using Aethra.Shared.Contracts.Vms;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.Infrastructure.Lookups;

/// <summary>
/// Implementación de <see cref="IVmWorkloadLookup"/> para el módulo Projects.
/// Lista las instancias que apuntan a una VM determinada.
/// </summary>
internal sealed class EfProjectsVmWorkloadLookup(ProjectsDbContext db) : IVmWorkloadLookup
{
    public async Task<IReadOnlyList<VmWorkloadRef>> ListOnVmAsync(string vmId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(vmId))
        {
            return [];
        }

        var instances = await db.Instances
            .AsNoTracking()
            .Where(i => i.TargetVmId == vmId)
            .Select(i => new { i.Id, i.ContainerName })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return instances
            .Select(i => new VmWorkloadRef("instance", i.Id.ToString(), i.ContainerName))
            .ToList();
    }
}
