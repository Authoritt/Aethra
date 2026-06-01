using Aethra.Modules.Proxy.Domain;
using Aethra.Modules.Proxy.Infrastructure.Yarp;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Kernel.Primitives;
using Aethra.Shared.Kernel.Time;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Proxy.Infrastructure.Handlers;

/// <summary>
/// Suscriptor cross-module: cuando Projects provisiona una <c>Instance</c>, el Proxy crea (o
/// actualiza, idempotentemente) la <c>Route</c> YARP que apunta al contenedor. Prioriza el
/// <c>CustomDomain</c> si existe; si no, usa el <c>AutoHostname</c>. Sin hostname o sin puerto
/// la Instance es headless ⇒ se ignora.
///
/// La operación es idempotente: si ya existe una Route para el hostname, se actualiza el backend
/// (caso típico: re-emisión del evento o lifecycle de reinstall). Tras persistir se llama
/// <see cref="IProxyConfigService.Reload"/> para que YARP recoja la nueva config sin restart.
/// </summary>
internal sealed class InstanceProvisionedHandler(
    ProxyDbContext db,
    IProxyConfigService config,
    IClock clock,
    ILogger<InstanceProvisionedHandler> logger)
    : INotificationHandler<InstanceProvisionedIntegrationEvent>
{
    public async Task Handle(InstanceProvisionedIntegrationEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var hostnameValue = notification.CustomDomain ?? notification.AutoHostname;
        if (string.IsNullOrWhiteSpace(hostnameValue) || notification.PrimaryPort is null)
        {
            logger.LogInformation(
                "InstanceProvisioned {Id}: sin hostname/port — Instance headless, no se crea Route",
                notification.InstanceId);
            return;
        }

        var hostnameResult = Hostname.Create(hostnameValue);
        if (hostnameResult.IsFailure)
        {
            logger.LogWarning(
                "InstanceProvisioned {Id}: hostname inválido '{Hostname}' ({Code}) — Route NO creada",
                notification.InstanceId, hostnameValue, hostnameResult.Error.Code);
            return;
        }
        var hostname = hostnameResult.Value;

        var backendUrl = $"http://{notification.ContainerName}:{notification.PrimaryPort}";

        // Buscar Route existente con el mismo hostname. Si existe, actualizar backend (idempotencia
        // garantizada para re-deliveries del evento). Si no, crear nueva.
        var existing = await db.Routes
            .FirstOrDefaultAsync(r => r.Hostname == hostname, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.UpdateBackend(backendUrl, clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            config.Reload();
            logger.LogInformation(
                "InstanceProvisioned {Id}: Route {RouteId} actualizada → {Hostname} → {Backend}",
                notification.InstanceId, existing.Id, hostname.Value, backendUrl);
            return;
        }

        // tlsEnabled=true: con BaseDomain con wildcard configurado el cert ya existe; con custom
        // domain F9.6 emitirá el certificado vía Let's Encrypt en flow separado. En cualquier
        // caso la Route se marca TLS-on para que YARP no acepte HTTP plano en producción.
        Route route;
        try
        {
            route = Route.Create(hostname, backendUrl, tlsEnabled: true, clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex,
                "InstanceProvisioned {Id}: backend_url inválido '{Backend}' — Route NO creada",
                notification.InstanceId, backendUrl);
            return;
        }

        db.Routes.Add(route);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        config.Reload();

        logger.LogInformation(
            "InstanceProvisioned {Id}: Route {RouteId} creada → {Hostname} → {Backend}",
            notification.InstanceId, route.Id, hostname.Value, backendUrl);
    }
}
