using Aethra.Shared.Contracts.Containers;
using Aethra.Shared.Contracts.Projects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Deployments.Infrastructure.Handlers;

/// <summary>
/// Suscriptor cross-module del teardown de Instance: elimina en el satélite los contenedores
/// desplegados de la instance (<c>{slug}-{servicio}</c> + el ContainerName legacy, que viajan en el
/// evento). Antes, borrar una Instance dejaba los contenedores CORRIENDO — fuga de recursos/disco.
/// Best-effort: si el satélite no está conectado o el RPC falla, loguea y sigue (no rompe el resto
/// del teardown ni reintenta indefinidamente).
/// </summary>
internal sealed class InstanceRemovedContainerCleanupHandler(
    ISatelliteRpcClient satelliteClient,
    ILogger<InstanceRemovedContainerCleanupHandler> logger)
    : INotificationHandler<InstanceRemovedIntegrationEvent>
{
    public async Task Handle(InstanceRemovedIntegrationEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (string.IsNullOrWhiteSpace(notification.TargetVmId)
            || notification.ContainerNames is not { Count: > 0 } containerNames)
        {
            return;
        }

        foreach (var name in containerNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal))
        {
            try
            {
                await satelliteClient
                    .SendRemoveAsync(notification.TargetVmId!, name, force: true, cancellationToken)
                    .ConfigureAwait(false);
                logger.LogInformation(
                    "InstanceRemoved {Id}: contenedor '{Name}' eliminado en {Vm}",
                    notification.InstanceId, name, notification.TargetVmId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "InstanceRemoved {Id}: no se pudo eliminar contenedor '{Name}' en {Vm} (best-effort)",
                    notification.InstanceId, name, notification.TargetVmId);
            }
        }
    }
}
