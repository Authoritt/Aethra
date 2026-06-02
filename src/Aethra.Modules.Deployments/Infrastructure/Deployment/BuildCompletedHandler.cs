using Aethra.Modules.Deployments.Domain.Deployment;
using Aethra.Shared.Contracts.Deployments;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Kernel.Time;
using MediatR;
using Microsoft.EntityFrameworkCore;
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
/// Idempotencia F9.10 D2: el outbox es at-least-once. Si el evento se entrega 2 veces, sin check
/// crearíamos 2N <see cref="Deployment"/> duplicados (uno por cada Instance × cada entrega). Para
/// evitarlo, antes de crear cada Deployment chequeamos en BD si ya existe uno para
/// <c>(BuildId, InstanceId)</c>. Si existe, skipeamos esa Instance y logueamos.
///
/// TODO técnico pendiente: añadir UNIQUE constraint <c>(build_id, instance_id)</c> en la tabla
/// <c>deployments.deployments</c> via nueva migración. Eso convierte la idempotencia en garantía
/// de schema (race condition imposible incluso con dos handlers concurrentes); el check en código
/// actual sigue siendo correcto pero teóricamente sufre TOCTOU si dos entregas del outbox se
/// procesaran en paralelo (en F9.10 el outbox publisher es single-threaded, así que no aplica).
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
        var skipped = 0;
        foreach (var instance in instances)
        {
            // F9.10 D2: idempotencia outbox at-least-once — si ya hay un Deployment para
            // (build, instance) no creamos otro. Caso típico: outbox publisher reintenta
            // tras un fallo transitorio del bus o del downstream y el evento se redelivera.
            var alreadyExists = await db.Deployments
                .AnyAsync(d => d.BuildId == notification.BuildId && d.InstanceId == instance.InstanceId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (alreadyExists)
            {
                logger.LogInformation(
                    "Deployment ya existe para (build={BuildId}, instance={InstanceId}), idempotencia outbox — skip",
                    notification.BuildId, instance.InstanceId);
                skipped++;
                continue;
            }

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

        if (queuedIds.Count == 0)
        {
            logger.LogInformation(
                "BuildCompleted {BuildId} (template={Template}): {Skipped} Instances ya tenían deployment — no hay fan-out nuevo",
                notification.BuildId, notification.TemplateId, skipped);
            return;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Notificar al worker tras commit en BD: si el commit falla, no encolamos nada.
        foreach (var id in queuedIds)
        {
            await queue.EnqueueAsync(id, cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "BuildCompleted {BuildId} (template={Template}): fan-out a {Count} Instances con auto-deploy (skipped={Skipped})",
            notification.BuildId, notification.TemplateId, queuedIds.Count, skipped);
    }
}
