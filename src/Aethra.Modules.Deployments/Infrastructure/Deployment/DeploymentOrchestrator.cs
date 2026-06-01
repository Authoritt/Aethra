using System.Globalization;
using Aethra.Modules.Deployments.Domain.Deployment;
using Aethra.Shared.Contracts.Containers;
using Aethra.Shared.Contracts.Deployments;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Contracts.Settings;
using Aethra.Shared.Infrastructure.Outbox;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Deployments.Infrastructure.Deployment;

/// <summary>
/// Implementación del orquestador de deployments. F9.3 entrega un pipeline en MODO DRY-RUN:
///
/// <list type="bullet">
///   <item>La state machine avanza completa (Pending → Pulling → Starting → Healthcheck →
///         Swapping → Completed), se persisten transiciones y se emiten logs reales.</item>
///   <item>Los pasos por <see cref="ISatelliteRpcClient"/> están stubeados
///         (<see cref="NotImplementedException"/>). Lo intentamos para validar el call site y,
///         si lanza, registramos un warning ("satellite RPC pendiente F9.3.5") y seguimos.</item>
///   <item>El atomic swap se materializa emitiendo <see cref="DeploymentCompletedIntegrationEvent"/>
///         al outbox; el módulo Proxy lo consume y actualiza la Route. Por ahora el evento contiene
///         el <c>ContainerName</c> derivado de la Instance — F9.4 cableará el hostname auto-derivado
///         (Settings.BaseDomain + slug) en el subscriber del proxy.</item>
///   <item>Las env vars resueltas en Starting son un mock: F9.5 cableará la resolución real
///         Project → Template → Client → Instance.</item>
/// </list>
///
/// Si falla en Swapping o post-Swap, se intenta <see cref="DoRollbackAsync"/>: si el contenedor
/// previo existe, se le manda comando al satélite para restaurarlo (dry-run: skipea con warn) y
/// el deployment cierra como <see cref="DeploymentStatus.RolledBack"/> en vez de <c>Failed</c>.
/// </summary>
public sealed class DeploymentOrchestrator(
    DeploymentsDbContext db,
    IInstanceLookup instanceLookup,
    ITenantContext tenantContext,
    IEnvVarWriter envVarWriter,
    IIntegrationCredentialResolver credentialResolver,
    IBaseDomainProvider baseDomainProvider,
    ISatelliteRpcClient satelliteClient,
    IOutboxWriter<DeploymentsDbContext> outbox,
    IClock clock,
    ILogger<DeploymentOrchestrator> logger) : IDeploymentOrchestrator
{
    // F9.4: cuando exista una credencial para el registry interno, el orquestador la
    // descifrará vía credentialResolver y la inyectará en el satellite RPC.
    private const string InternalRegistryCredentialName = "registry:internal";

    public async Task RunAsync(DeploymentId deploymentId, CancellationToken ct)
    {
        // envVarWriter / tenantContext / baseDomainProvider: dependencias que F9.5 / F9.4 cablearán
        // en la resolución real. En F9.3 las referenciamos para asegurar el wire DI completo y
        // dejar el log claro sobre quién provee qué.
        _ = envVarWriter;
        _ = tenantContext;

        var deployment = await db.Deployments
            .Include(d => d.Logs)
            .FirstOrDefaultAsync(d => d.Id == deploymentId, ct)
            .ConfigureAwait(false);

        if (deployment is null)
        {
            logger.LogWarning("DeploymentOrchestrator: deployment {Id} no existe — skip",
                deploymentId);
            return;
        }

        if (deployment.Status.IsTerminal())
        {
            logger.LogInformation(
                "DeploymentOrchestrator: deployment {Id} ya está en estado terminal {Status} — skip",
                deploymentId, deployment.Status);
            return;
        }

        var instance = await instanceLookup.GetByIdAsync(deployment.InstanceId, ct)
            .ConfigureAwait(false);
        if (instance is null)
        {
            FailAndPersist(deployment, "instance_not_found",
                $"Instance '{deployment.InstanceId}' no existe (¿borrada tras encolar?).");
            await PersistAndPublishFailureAsync(deployment, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            // === Capturar contenedor previo (para rollback) ===
            // F9.4 hará el lookup real via satelliteClient.SendListContainersAsync para localizar
            // el contenedor previo del Instance. En dry-run dejamos OldContainerId vacío.
            deployment.RecordOldContainer(string.Empty, string.Empty, clock.UtcNow);
            deployment.AppendLog(DeploymentLogLevel.Info, "pending",
                "dry-run: no se consulta satellite para localizar contenedor previo "
                + "(F9.4 cableará SendListContainersAsync).",
                clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // === Pulling ===
            deployment.Transition(DeploymentStatus.Pulling, clock.UtcNow);
            await TryPullImageAsync(deployment, instance, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // === Starting ===
            deployment.Transition(DeploymentStatus.Starting, clock.UtcNow);
            var newContainerId = await TryRunContainerAsync(deployment, instance, ct)
                .ConfigureAwait(false);
            deployment.RecordNewContainer(newContainerId, clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // === Healthcheck ===
            deployment.Transition(DeploymentStatus.Healthcheck, clock.UtcNow);
            deployment.AppendLog(DeploymentLogLevel.Info, "healthcheck",
                "dry-run: healthcheck inmediato pasado (F9.4 esperará HEALTHCHECK real del contenedor).",
                clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // === Swapping (atomic) ===
            deployment.Transition(DeploymentStatus.Swapping, clock.UtcNow);
            deployment.AppendLog(DeploymentLogLevel.Info, "swapping",
                "Emitiendo DeploymentCompletedIntegrationEvent → módulo Proxy actualizará Route.",
                clock.UtcNow);

            // El subscriber del proxy verá este evento y resolverá/creará la Route apuntando al
            // contenedor nuevo. La actualización es transaccional vía outbox: si SaveChanges
            // falla aquí, el evento NO se emite (semántica all-or-nothing del outbox).
            await outbox.EnqueueAsync(new DeploymentCompletedIntegrationEvent(
                DeploymentId: deployment.Id.ToString(),
                InstanceId: deployment.InstanceId,
                NewImageRef: deployment.NewImageRef,
                ContainerName: instance.ContainerName,
                ContainerPort: instance.PrimaryContainerPort,
                CompletedAt: clock.UtcNow), ct).ConfigureAwait(false);

            // === Complete ===
            deployment.Complete(clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            logger.LogInformation(
                "Deployment {Id} (build={Build}, instance={Instance}) completado en dry-run",
                deployment.Id, deployment.BuildId, deployment.InstanceId);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "DeploymentOrchestrator: cancelado durante deployment {Id}", deployment.Id);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "DeploymentOrchestrator: falla inesperada en deployment {Id}", deployment.Id);
            FailAndPersist(deployment, "internal_error", ex.Message);

            // Si la falla ocurrió en Swapping o post-swap, intentamos rollback al contenedor previo.
            if (deployment.FailedAtStage is DeploymentStatus.Swapping
                && !string.IsNullOrWhiteSpace(deployment.OldContainerId))
            {
                await DoRollbackAsync(deployment, instance.TargetVmId, ct).ConfigureAwait(false);
            }

            await PersistAndPublishFailureAsync(deployment, ct).ConfigureAwait(false);
        }
    }

    private async Task TryPullImageAsync(Domain.Deployment.Deployment deployment,
        InstanceForDeployView instance, CancellationToken ct)
    {
        // F9.4 cableará el pull real al satélite. SendRunAsync ya pulea internamente, pero
        // dejamos el log de Pulling explícito para que la UI lo muestre como step distinto.
        var credentialExists = await credentialResolver
            .ExistsAsync(InternalRegistryCredentialName, ct).ConfigureAwait(false);
        if (!credentialExists)
        {
            deployment.AppendLog(DeploymentLogLevel.Warn, "pulling",
                $"Credencial '{InternalRegistryCredentialName}' no configurada — "
                + "F9.4 la requerirá para pull desde el registry interno.",
                clock.UtcNow);
        }
        deployment.AppendLog(DeploymentLogLevel.Info, "pulling",
            $"dry-run: pull simulado {deployment.NewImageRef} → VM={instance.TargetVmId}",
            clock.UtcNow);

        // Intentamos llamar al satélite. F9.3 lanza NotImplementedException; lo dejamos
        // documentado en el log y seguimos.
        try
        {
            // Convención: pull se hace lazy dentro de SendRunAsync. Si tuviéramos un SendPullAsync
            // discreto en el futuro, iría aquí. Por ahora marcamos el step como dry-run.
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (NotImplementedException)
        {
            deployment.AppendLog(DeploymentLogLevel.Warn, "pulling",
                "satellite RPC pendiente F9.3.5: continuamos en dry-run",
                clock.UtcNow);
        }
    }

    private async Task<string> TryRunContainerAsync(Domain.Deployment.Deployment deployment,
        InstanceForDeployView instance, CancellationToken ct)
    {
        // F9.5 cableará la resolución real Project → Template → Client → Instance para construir
        // el diccionario de env vars. En dry-run solo logueamos.
        deployment.AppendLog(DeploymentLogLevel.Info, "starting",
            "dry-run: env vars mock (F9.5 cableará resolución Project→Template→Client→Instance).",
            clock.UtcNow);

        var spec = new RunSpec(
            ContainerName: instance.ContainerName,
            ImageRef: deployment.NewImageRef,
            Env: new Dictionary<string, string>(StringComparer.Ordinal),
            Ports: [],
            Volumes: [],
            Command: null,
            Healthcheck: null,
            NetworkName: null,
            RestartPolicy: "unless-stopped");

        try
        {
            var result = await satelliteClient
                .SendRunAsync(instance.TargetVmId, spec, pullFrom: null, ct: ct)
                .ConfigureAwait(false);

            if (!result.Success || string.IsNullOrWhiteSpace(result.ContainerId))
            {
                throw new InvalidOperationException(
                    result.ErrorMessage ?? "Satellite SendRunAsync devolvió fallo sin mensaje.");
            }
            deployment.AppendLog(DeploymentLogLevel.Info, "starting",
                $"Contenedor levantado por satélite: {result.ContainerId}", clock.UtcNow);
            return result.ContainerId;
        }
        catch (NotImplementedException)
        {
            deployment.AppendLog(DeploymentLogLevel.Warn, "starting",
                "satellite RPC pendiente F9.3.5: simulamos container id estable para el deployment",
                clock.UtcNow);
            // ID determinista basado en el deployment para que el resto del flujo funcione.
            return $"dry-run-{deployment.Id}".ToLowerInvariant();
        }
    }

    private async Task DoRollbackAsync(Domain.Deployment.Deployment deployment, string targetVmId,
        CancellationToken ct)
    {
        deployment.AppendLog(DeploymentLogLevel.Warn, "swapping",
            $"Intentando rollback: restaurar contenedor previo {deployment.OldContainerId}",
            clock.UtcNow);
        try
        {
            // F9.4 cableará el restart real. En F9.3 simplemente intentamos y dejamos el log.
            await satelliteClient
                .SendRunAsync(targetVmId,
                    new RunSpec(
                        ContainerName: deployment.OldContainerId ?? string.Empty,
                        ImageRef: deployment.OldImageRef ?? string.Empty,
                        Env: new Dictionary<string, string>(StringComparer.Ordinal),
                        Ports: [],
                        Volumes: [],
                        Command: null,
                        Healthcheck: null,
                        NetworkName: null,
                        RestartPolicy: "unless-stopped"),
                    pullFrom: null,
                    ct: ct)
                .ConfigureAwait(false);
            deployment.Rollback(clock.UtcNow);
            logger.LogInformation("Rollback OK para deployment {Id}", deployment.Id);
        }
        catch (NotImplementedException)
        {
            deployment.AppendLog(DeploymentLogLevel.Warn, "swapping",
                "Rollback: satellite RPC pendiente F9.3.5 — el contenedor previo NO se restauró en BD; "
                + "el deployment queda en Failed (no RolledBack).",
                clock.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Rollback falló para deployment {Id}", deployment.Id);
            deployment.AppendLog(DeploymentLogLevel.Error, "swapping",
                $"Rollback falló: {ex.Message}", clock.UtcNow);
        }
    }

    private void FailAndPersist(Domain.Deployment.Deployment deployment, string code, string message)
    {
        if (!deployment.Status.IsTerminal())
        {
            deployment.Fail(code, message, clock.UtcNow);
        }
    }

    private async Task PersistAndPublishFailureAsync(Domain.Deployment.Deployment deployment, CancellationToken ct)
    {
        await outbox.EnqueueAsync(new DeploymentFailedIntegrationEvent(
            DeploymentId: deployment.Id.ToString(),
            InstanceId: deployment.InstanceId,
            ErrorCode: deployment.ErrorCode ?? "unknown",
            ErrorMessage: deployment.ErrorMessage ?? string.Empty,
            FailedAt: clock.UtcNow), ct).ConfigureAwait(false);

        // baseDomainProvider: anotamos su disponibilidad en el log de errores para que el
        // operador detecte temprano la falta de BaseDomain (necesaria para auto-hostname F9.4).
        var baseDomain = await baseDomainProvider.GetActiveAsync(ct).ConfigureAwait(false);
        if (baseDomain is null)
        {
            deployment.AppendLog(DeploymentLogLevel.Warn, "failed",
                "Nota: no hay BaseDomain activo configurado (F9.4 lo requiere para auto-hostname).",
                clock.UtcNow);
        }
        else
        {
            deployment.AppendLog(DeploymentLogLevel.Info, "failed",
                string.Create(CultureInfo.InvariantCulture,
                    $"BaseDomain activo: {baseDomain.Hostname} (wildcard={baseDomain.WildcardConfigured})"),
                clock.UtcNow);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
