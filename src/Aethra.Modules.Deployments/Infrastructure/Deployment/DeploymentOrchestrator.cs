using System.Globalization;
using Aethra.Modules.Deployments.Domain.Deployment;
using Aethra.Shared.Contracts.Containers;
using Aethra.Shared.Contracts.Deployments;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Contracts.Settings;
using Aethra.Shared.Infrastructure.Outbox;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Deployments.Infrastructure.Deployment;

/// <summary>
/// Implementación del orquestador de deployments. F10.1c entrega el pipeline REAL:
///
/// <list type="bullet">
///   <item>La state machine avanza completa (Pending → Pulling → Starting → Healthcheck →
///         Swapping → Completed), persistiendo transiciones y logs.</item>
///   <item><b>Pending</b>: localiza el contenedor previo del Instance vía
///         <see cref="ISatelliteRpcClient.SendListContainersAsync"/> (para rollback).</item>
///   <item><b>Starting</b>: resuelve las env vars runtime (cascade Project → Template → Client →
///         Instance + secretos descifrados vía <see cref="IEnvironmentResolver"/>), retira el
///         contenedor previo para liberar el nombre estable, y arranca el nuevo en la red Docker
///         compartida (<c>_appNetwork</c>) con su puerto primario expuesto.</item>
///   <item><b>Healthcheck</b>: hace polling del estado del contenedor en el satélite hasta que
///         queda <c>running</c>/<c>healthy</c> o se agota el tiempo.</item>
///   <item><b>Swapping</b>: emite <see cref="DeploymentCompletedIntegrationEvent"/> al outbox; el
///         módulo Proxy crea/actualiza la Route YARP apuntando a
///         <c>http://{containerName}:{port}</c> dentro de la red compartida.</item>
/// </list>
///
/// Si el healthcheck falla, se retira el contenedor nuevo y, si había uno previo, se restaura
/// (<see cref="DoRollbackAsync"/>) para no dejar la app caída; el deployment cierra como
/// <see cref="DeploymentStatus.RolledBack"/> en vez de <c>Failed</c>.
/// </summary>
public sealed class DeploymentOrchestrator(
    DeploymentsDbContext db,
    IInstanceLookup instanceLookup,
    IEnvironmentResolver envResolver,
    IIntegrationCredentialResolver credentialResolver,
    IBaseDomainProvider baseDomainProvider,
    ISatelliteRpcClient satelliteClient,
    IOutboxWriter<DeploymentsDbContext> outbox,
    IConfiguration configuration,
    IClock clock,
    ILogger<DeploymentOrchestrator> logger) : IDeploymentOrchestrator
{
    // Si en el futuro se hace pull desde un registry interno, el orquestador descifrará esta
    // credencial vía credentialResolver y la inyectará en el satellite RPC.
    private const string InternalRegistryCredentialName = "registry:internal";

    // Red Docker compartida por el central (YARP) y los contenedores de las apps. DEBE ser una
    // red definida por usuario (no la bridge default) para que el proxy resuelva el contenedor por
    // nombre DNS (http://{containerName}:{port}). Configurable vía Deployments:AppNetwork.
    private readonly string _appNetwork =
        configuration["Deployments:AppNetwork"] is { Length: > 0 } n ? n : "aethra-net";

    // Healthcheck post-arranque: cuántas veces consultamos el estado del contenedor y cada cuánto.
    private const int HealthcheckMaxAttempts = 20;
    private static readonly TimeSpan HealthcheckPollInterval = TimeSpan.FromSeconds(3);

    public async Task RunAsync(DeploymentId deploymentId, CancellationToken ct)
    {
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
            // Consultamos al satélite los contenedores existentes y localizamos el del Instance por
            // nombre. Si existe, guardamos su id+imagen para poder restaurarlo en rollback (luego se
            // retira en Starting para liberar el nombre estable del contenedor nuevo).
            var previous = await TryFindExistingContainerAsync(instance, ct).ConfigureAwait(false);
            if (previous is not null)
            {
                deployment.RecordOldContainer(previous.Id, previous.Image, clock.UtcNow);
                deployment.AppendLog(DeploymentLogLevel.Info, "pending",
                    $"Contenedor previo localizado: {previous.Name} ({previous.Image}) — se reemplazará.",
                    clock.UtcNow);
            }
            else
            {
                deployment.RecordOldContainer(string.Empty, string.Empty, clock.UtcNow);
                deployment.AppendLog(DeploymentLogLevel.Info, "pending",
                    "Sin contenedor previo para este Instance (primer deploy).",
                    clock.UtcNow);
            }
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // === Pulling ===
            deployment.Transition(DeploymentStatus.Pulling, clock.UtcNow);
            await TryPullImageAsync(deployment, instance, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // === Starting ===
            deployment.Transition(DeploymentStatus.Starting, clock.UtcNow);

            // Liberar el nombre estable del contenedor si había uno previo (redeploy). Mantener el
            // mismo nombre permite que el proxy conserve su Route (http://{name}:{port}). Implica un
            // breve downtime; si el nuevo no pasa healthcheck se re-levanta el previo (rollback).
            if (!string.IsNullOrWhiteSpace(deployment.OldContainerId))
            {
                await TryRemoveContainerAsync(instance.TargetVmId, instance.ContainerName, ct)
                    .ConfigureAwait(false);
                deployment.AppendLog(DeploymentLogLevel.Info, "starting",
                    $"Contenedor previo {instance.ContainerName} retirado para liberar el nombre.",
                    clock.UtcNow);
            }

            var newContainerId = await TryRunContainerAsync(deployment, instance, ct)
                .ConfigureAwait(false);
            deployment.RecordNewContainer(newContainerId, clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // === Healthcheck ===
            deployment.Transition(DeploymentStatus.Healthcheck, clock.UtcNow);
            var healthy = await PollContainerHealthyAsync(deployment, instance, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            if (!healthy)
            {
                // El contenedor nuevo no quedó sano: lo retiramos y, si había uno previo, lo
                // restauramos (rollback) para no dejar la app caída.
                await TryRemoveContainerAsync(instance.TargetVmId, instance.ContainerName, ct)
                    .ConfigureAwait(false);
                FailAndPersist(deployment, "healthcheck_failed",
                    "El contenedor no alcanzó estado 'running'/'healthy' dentro del tiempo de espera.");
                if (!string.IsNullOrWhiteSpace(deployment.OldImageRef)
                    && !string.IsNullOrWhiteSpace(deployment.OldContainerId))
                {
                    await DoRollbackAsync(deployment, instance, ct).ConfigureAwait(false);
                }
                await PersistAndPublishFailureAsync(deployment, ct).ConfigureAwait(false);
                return;
            }

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
                await DoRollbackAsync(deployment, instance, ct).ConfigureAwait(false);
            }

            await PersistAndPublishFailureAsync(deployment, ct).ConfigureAwait(false);
        }
    }

    private async Task TryPullImageAsync(Domain.Deployment.Deployment deployment,
        InstanceForDeployView instance, CancellationToken ct)
    {
        // El pull real lo hace el satélite de forma lazy dentro de SendRunAsync
        // (EnsureImageAvailable): si la imagen no está local hace pull anónimo. En single-VM la
        // imagen ya está local porque el build corrió en el mismo satélite, así que no hay pull.
        var credentialExists = await credentialResolver
            .ExistsAsync(InternalRegistryCredentialName, ct).ConfigureAwait(false);
        if (!credentialExists)
        {
            deployment.AppendLog(DeploymentLogLevel.Info, "pulling",
                $"Sin credencial '{InternalRegistryCredentialName}': imagen local (single-VM) o "
                + "pull anónimo. El satélite resolverá la imagen al arrancar.",
                clock.UtcNow);
        }
        deployment.AppendLog(DeploymentLogLevel.Info, "pulling",
            $"Imagen objetivo {deployment.NewImageRef} → VM={instance.TargetVmId}",
            clock.UtcNow);
    }

    private async Task<string> TryRunContainerAsync(Domain.Deployment.Deployment deployment,
        InstanceForDeployView instance, CancellationToken ct)
    {
        // Resolución real de env vars: cascade Project → Template → Client → Instance + secretos
        // descifrados (F10.1c). El diccionario NO se loguea (puede contener secretos).
        var env = await ResolveEnvAsync(instance, ct).ConfigureAwait(false);
        deployment.AppendLog(DeploymentLogLevel.Info, "starting",
            $"Env vars resueltas: {env.Count} (cascade Project→Template→Client→Instance).",
            clock.UtcNow);

        var spec = new RunSpec(
            ContainerName: instance.ContainerName,
            ImageRef: deployment.NewImageRef,
            Env: env,
            Ports: BuildPortBindings(instance),
            Volumes: [],
            Command: null,
            Healthcheck: null,
            NetworkName: _appNetwork,
            RestartPolicy: "unless-stopped");

        deployment.AppendLog(DeploymentLogLevel.Info, "starting",
            $"Arrancando {instance.ContainerName} en red {_appNetwork} (puerto "
            + $"{instance.PrimaryContainerPort?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}).",
            clock.UtcNow);

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
        catch (SatelliteNotConnectedException ex)
        {
            // Propagar para que RunAsync marque el deployment como Failed con errorCode estable.
            throw new InvalidOperationException($"no_satellite: {ex.Message}", ex);
        }
    }

    private Task<IReadOnlyDictionary<string, string>> ResolveEnvAsync(
        InstanceForDeployView instance, CancellationToken ct)
        => envResolver.ResolveRuntimeEnvAsync(
            new EnvironmentScopeChain(
                ProjectId: instance.ProjectId,
                TemplateId: instance.TemplateId,
                ClientId: instance.ClientId,
                InstanceId: instance.InstanceId),
            ct);

    private static IReadOnlyList<PortBinding> BuildPortBindings(InstanceForDeployView instance)
        => instance.PrimaryContainerPort is int containerPort
            ? [new PortBinding(ContainerPort: containerPort, HostPort: null, Protocol: "tcp")]
            : [];

    private async Task<ContainerInfo?> TryFindExistingContainerAsync(
        InstanceForDeployView instance, CancellationToken ct)
    {
        try
        {
            var containers = await satelliteClient
                .SendListContainersAsync(instance.TargetVmId, ct).ConfigureAwait(false);
            return containers.FirstOrDefault(c =>
                string.Equals(c.Name, instance.ContainerName, StringComparison.Ordinal));
        }
        catch (SatelliteNotConnectedException)
        {
            // Sin satélite no hay contenedor previo que capturar; el run posterior fallará con
            // no_satellite de forma explícita.
            return null;
        }
    }

    private async Task TryRemoveContainerAsync(string vmId, string containerNameOrId, CancellationToken ct)
    {
        try
        {
            await satelliteClient.SendRemoveAsync(vmId, containerNameOrId, force: true, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo retirar el contenedor {Name} en VM {Vm}",
                containerNameOrId, vmId);
        }
    }

    private async Task<bool> PollContainerHealthyAsync(Domain.Deployment.Deployment deployment,
        InstanceForDeployView instance, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= HealthcheckMaxAttempts; attempt++)
        {
            ContainerInfo? container;
            try
            {
                var containers = await satelliteClient
                    .SendListContainersAsync(instance.TargetVmId, ct).ConfigureAwait(false);
                container = containers.FirstOrDefault(c =>
                    string.Equals(c.Name, instance.ContainerName, StringComparison.Ordinal));
            }
            catch (SatelliteNotConnectedException)
            {
                deployment.AppendLog(DeploymentLogLevel.Error, "healthcheck",
                    "Satélite desconectado durante el healthcheck.", clock.UtcNow);
                return false;
            }

            var status = container?.Status ?? string.Empty;
            // Docker reporta "Up X" para corriendo; "(healthy)"/"(unhealthy)" si la imagen declara
            // HEALTHCHECK. Exited/Restarting => no sano (crash-loop o salida temprana).
            if (status.Contains("unhealthy", StringComparison.OrdinalIgnoreCase)
                || status.StartsWith("Exited", StringComparison.OrdinalIgnoreCase)
                || status.Contains("Restarting", StringComparison.OrdinalIgnoreCase))
            {
                deployment.AppendLog(DeploymentLogLevel.Warn, "healthcheck",
                    $"Intento {attempt}/{HealthcheckMaxAttempts}: estado '{status}'.", clock.UtcNow);
            }
            else if (status.StartsWith("Up", StringComparison.OrdinalIgnoreCase)
                || status.Contains("healthy", StringComparison.OrdinalIgnoreCase)
                || status.Contains("running", StringComparison.OrdinalIgnoreCase))
            {
                deployment.AppendLog(DeploymentLogLevel.Info, "healthcheck",
                    $"Contenedor sano tras {attempt} intento(s): '{status}'.", clock.UtcNow);
                return true;
            }

            await Task.Delay(HealthcheckPollInterval, ct).ConfigureAwait(false);
        }

        deployment.AppendLog(DeploymentLogLevel.Error, "healthcheck",
            $"El contenedor no quedó sano tras {HealthcheckMaxAttempts} intentos.", clock.UtcNow);
        return false;
    }

    private async Task DoRollbackAsync(Domain.Deployment.Deployment deployment,
        InstanceForDeployView instance, CancellationToken ct)
    {
        deployment.AppendLog(DeploymentLogLevel.Warn, "swapping",
            $"Rollback: restaurando imagen previa {deployment.OldImageRef} como {instance.ContainerName}.",
            clock.UtcNow);
        try
        {
            var env = await ResolveEnvAsync(instance, ct).ConfigureAwait(false);
            var result = await satelliteClient
                .SendRunAsync(instance.TargetVmId,
                    new RunSpec(
                        ContainerName: instance.ContainerName,
                        ImageRef: deployment.OldImageRef ?? string.Empty,
                        Env: env,
                        Ports: BuildPortBindings(instance),
                        Volumes: [],
                        Command: null,
                        Healthcheck: null,
                        NetworkName: _appNetwork,
                        RestartPolicy: "unless-stopped"),
                    pullFrom: null,
                    ct: ct)
                .ConfigureAwait(false);
            if (result.Success)
            {
                deployment.Rollback(clock.UtcNow);
                logger.LogInformation("Rollback OK para deployment {Id}", deployment.Id);
            }
            else
            {
                deployment.AppendLog(DeploymentLogLevel.Error, "swapping",
                    $"Rollback falló al re-arrancar el contenedor previo: {result.ErrorMessage}",
                    clock.UtcNow);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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
