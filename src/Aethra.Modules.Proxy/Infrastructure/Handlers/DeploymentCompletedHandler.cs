using Aethra.Modules.Proxy.Domain;
using Aethra.Modules.Proxy.Infrastructure.Yarp;
using Aethra.Shared.Contracts.Deployments;
using Aethra.Shared.Contracts.Settings;
using Aethra.Shared.Kernel.Primitives;
using Aethra.Shared.Kernel.Time;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Proxy.Infrastructure.Handlers;

/// <summary>
/// Suscriptor cross-module: cuando un <c>Deployment</c> completa con éxito (atomic swap), el
/// módulo Proxy actualiza/crea la <c>Route</c> YARP para apuntar al contenedor nuevo. El
/// resultado es un hot-reload sin downtime: YARP descubre la nueva config en el próximo
/// <see cref="DatabaseProxyConfigProvider.Reload"/>.
///
/// <para>Flujo:</para>
/// <list type="number">
///   <item>Construye la <c>BackendUrl</c> a partir de <see cref="DeploymentCompletedIntegrationEvent.ContainerName"/>
///         + <see cref="DeploymentCompletedIntegrationEvent.ContainerPort"/>.</item>
///   <item>Busca una <c>Route</c> existente cuyo backend apunte a este Instance (match por
///         containerName en la URL). Si existe, hace <c>UpdateBackend</c>.</item>
///   <item>Si no existe, intenta crear una Route nueva con hostname auto-derivado:
///         <c>{containerName}.{BaseDomain}</c>. Si <see cref="IBaseDomainProvider.GetActiveAsync"/>
///         devuelve null, deja la Route sin crear y logea un warning — F9.4 cableará el
///         hostname auto-derived.</item>
///   <item>Tras persistir, llama <see cref="IProxyConfigService.Reload"/> para que YARP recoja
///         la nueva config en caliente.</item>
/// </list>
///
/// <para>
/// La operación se hace con un único <c>SaveChangesAsync</c> al final del handler sobre el
/// <c>ProxyDbContext</c> (no hay <c>TransactionBehavior</c> registrado en el modelo modular
/// monolith de Aethra). Si el SaveChanges falla, el reload no se ejecuta y la actualización
/// del proxy queda como no-op.
/// </para>
/// </summary>
internal sealed class DeploymentCompletedHandler(
    ProxyDbContext db,
    IBaseDomainProvider baseDomainProvider,
    IProxyConfigService configService,
    IClock clock,
    ILogger<DeploymentCompletedHandler> logger)
    : INotificationHandler<DeploymentCompletedIntegrationEvent>
{
    public async Task Handle(DeploymentCompletedIntegrationEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        // Deploys headless (workers sin endpoint) no implican routing: si no hay containerName
        // o puerto, ignoramos el evento — el módulo Proxy solo gestiona rutas HTTP(S).
        if (string.IsNullOrWhiteSpace(notification.ContainerName) || notification.ContainerPort is null)
        {
            logger.LogInformation(
                "DeploymentCompleted {Id}: sin ContainerName/Port — Instance es headless, no se actualiza Route",
                notification.DeploymentId);
            return;
        }

        var backendUrl = $"http://{notification.ContainerName}:{notification.ContainerPort}";

        // 1) Buscar Route existente que apunte a este Instance. La heurística: el backend_url
        // contiene el container_name. Es estable porque ContainerName es único por host y la
        // BackendUrl se actualiza completa en cada deploy.
        var existing = await db.Routes
            .FirstOrDefaultAsync(r => r.BackendUrl.Contains(notification.ContainerName), cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.UpdateBackend(backendUrl, clock.UtcNow);
            existing.SetOperationalOwner("app_environment", notification.InstanceId, "deployment_completed", clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            configService.Reload();
            logger.LogInformation(
                "DeploymentCompleted {Id}: Route {RouteId} actualizada → {Backend}",
                notification.DeploymentId, existing.Id, backendUrl);
            return;
        }

        // 2) Crear Route nueva si tenemos BaseDomain activo. F9.4 cableará el slug del Instance
        // como subdominio; F9.3 hace stub con el ContainerName como fallback (que ya es único
        // por convención de Projects.Instance.ContainerName).
        var baseDomain = await baseDomainProvider.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (baseDomain is null)
        {
            logger.LogWarning(
                "DeploymentCompleted {Id}: no hay BaseDomain activo configurado, "
                + "F9.4 cableará el hostname auto-derived — Route NO creada para {Container}",
                notification.DeploymentId, notification.ContainerName);
            return;
        }

        // Auto-hostname stub: {containerName}.{baseDomain.Hostname}. F9.4 reemplazará por el
        // slug del Instance (ya soportado por Projects.Instance.AutoHostname) cuando se cablee
        // la propagación desde el Deployment event.
        var hostnameValue = $"{notification.ContainerName}.{baseDomain.Hostname}";
        var hostnameResult = Hostname.Create(hostnameValue);
        if (hostnameResult.IsFailure)
        {
            logger.LogWarning(
                "DeploymentCompleted {Id}: hostname auto-derived inválido '{Hostname}' "
                + "({Code}) — Route NO creada",
                notification.DeploymentId, hostnameValue, hostnameResult.Error.Code);
            return;
        }

        // tlsEnabled=false para el stub: la emisión Let's Encrypt se hace via flow separado
        // (RequestCertificateCommand) cuando el operador lo confirme. F9.4 puede preconfigurar
        // tls=true si BaseDomain.WildcardConfigured es true (cert wildcard ya disponible).
        var route = Route.Create(hostnameResult.Value, backendUrl, tlsEnabled: false, clock.UtcNow);
        route.SetOperationalOwner("app_environment", notification.InstanceId, "deployment_completed", clock.UtcNow);
        db.Routes.Add(route);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        configService.Reload();

        logger.LogInformation(
            "DeploymentCompleted {Id}: Route {RouteId} creada → {Hostname} → {Backend}",
            notification.DeploymentId, route.Id, hostnameResult.Value, backendUrl);
    }
}
