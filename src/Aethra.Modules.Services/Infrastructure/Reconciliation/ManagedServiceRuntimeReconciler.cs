using Aethra.Modules.Services.Domain;
using Aethra.Shared.Contracts.Containers;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Services.Infrastructure.Reconciliation;

internal sealed class ManagedServiceRuntimeReconciler(
    ServicesDbContext db,
    ISatelliteRpcClient satellite,
    IClock clock,
    ILogger<ManagedServiceRuntimeReconciler> logger)
{
    public async Task<int> ReconcileReadyServicesAsync(CancellationToken cancellationToken)
    {
        var services = await db.ManagedServices
            .Where(s => s.Status == ManagedServiceStatus.Ready)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var changed = 0;
        foreach (var group in services.GroupBy(s => s.TargetVmId, StringComparer.Ordinal))
        {
            IReadOnlyList<ContainerInfo> containers;
            try
            {
                containers = await satellite.SendListContainersAsync(group.Key, cancellationToken).ConfigureAwait(false);
            }
            catch (SatelliteNotConnectedException ex)
            {
                logger.LogDebug(ex, "No se reconcilian servicios gestionados en VM {VmId}: satelite desconectado", group.Key);
                continue;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Fallo reconciliando servicios gestionados en VM {VmId}", group.Key);
                continue;
            }

            foreach (var service in group)
            {
                var exists = containers.Any(c =>
                    string.Equals(c.Name, service.ContainerName, StringComparison.Ordinal));
                if (exists)
                {
                    continue;
                }

                service.MarkRuntimeContainerMissing(clock.UtcNow);
                changed++;
                logger.LogWarning(
                    "Servicio gestionado {ServiceId} ({Slug}) marcado Failed: contenedor {Container} no existe en VM {VmId}",
                    service.Id, service.Slug, service.ContainerName, service.TargetVmId);
            }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return changed;
    }
}
