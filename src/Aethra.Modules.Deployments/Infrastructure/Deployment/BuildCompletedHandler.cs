using Aethra.Modules.Deployments.Domain.Deployment;
using Aethra.Shared.Contracts.Deployments;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Kernel.Time;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Deployments.Infrastructure.Deployment;

/// <summary>
/// Suscriptor cross-module crítico del pipeline: cuando un <c>Build</c> completa con éxito (evento
/// publicado por <c>BuildOrchestrator</c> via outbox), este handler hace fan-out:
/// <list type="number">
///   <item>Lee <see cref="BuildCompletedIntegrationEvent.TemplateId"/>.</item>
///   <item>Resuelve, via <see cref="IInstanceLookup.FindByTemplateAsync"/> con <c>autoDeployOnly=true</c>,
///         las Instances configuradas para auto-deploy del Template.</item>
///   <item>Por cada Instance, crea un <see cref="Deployment"/> en estado <c>Pending</c> y lo
///         encola en el <see cref="IDeploymentJobQueue"/> para que el <see cref="DeploymentWorker"/>
///         lo procese.</item>
/// </list>
///
/// <para>
/// Vive en <c>Infrastructure/Deployment</c> (no en UseCases) porque es un side-effect interno de
/// integración cross-bounded-context, no un caso de uso invocado por presentación.
/// </para>
///
/// <para>
/// Idempotencia: si el Build se reprocesa (mismo evento delivered dos veces por el outbox), se
/// crearán deployments duplicados. F9.4 añadirá una check "no existe Deployment activo para esta
/// (instance, build) antes de encolar" — por ahora la convención <c>at-least-once</c> del outbox
/// se acepta como riesgo conocido para una plataforma single-user.
/// </para>
/// </summary>
internal sealed class BuildCompletedHandler(
    DeploymentsDbContext db,
    IInstanceLookup instanceLookup,
    IDeploymentJobQueue queue,
    IClock clock,
    ILogger<BuildCompletedHandler> logger)
    : INotificationHandler<BuildCompletedIntegrationEvent>
{
    public async Task Handle(BuildCompletedIntegrationEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var instances = await instanceLookup
            .FindByTemplateAsync(notification.TemplateId, autoDeployOnly: true, cancellationToken)
            .ConfigureAwait(false);

        if (instances.Count == 0)
        {
            logger.LogInformation(
                "BuildCompleted {BuildId} (template={Template}): 0 Instances con auto-deploy — no hay fan-out",
                notification.BuildId, notification.TemplateId);
            return;
        }

        var queuedIds = new List<DeploymentId>(instances.Count);
        foreach (var instance in instances)
        {
            var deployment = Domain.Deployment.Deployment.Queue(
                buildId: notification.BuildId,
                instanceId: instance.InstanceId,
                newImageRef: notification.ImageRef,
                trigger: DeploymentTrigger.BuildAutomatic,
                triggeredBy: null,
                now: clock.UtcNow);

            db.Deployments.Add(deployment);
            queuedIds.Add(deployment.Id);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Notificar al worker tras commit en BD: si el commit falla, no encolamos nada.
        foreach (var id in queuedIds)
        {
            await queue.EnqueueAsync(id, cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "BuildCompleted {BuildId} (template={Template}): fan-out a {Count} Instances con auto-deploy",
            notification.BuildId, notification.TemplateId, instances.Count);
    }
}
